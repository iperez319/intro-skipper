using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IntroSkipper.Analyzers;
using IntroSkipper.Configuration;
using IntroSkipper.Data;
using Jellyfin.Extensions;
using Microsoft.Extensions.Logging;

if (args.Length != 1)
{
    Console.WriteLine("Usage: ChromaprintAnalyzerTool <path>");
    return;
}

var directoryPath = args[0];

// var directoryPath = Console.ReadLine();

if (!Directory.Exists(directoryPath))
{
    Console.WriteLine("Directory does not exist.");
    return;
}

var logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<IntroSkipper.Analyzers.ChromaprintAnalyzer>();
var analyzer = new ChromaprintAnalyzer(logger);
var _analysisPercent = 0.25;
var _config = new PluginConfiguration();

List<QueuedEpisode> analysisQueue = new List<QueuedEpisode>();

string seasonDataFilePath = Path.Combine(directoryPath, "seasonData.json");
SeasonData seasonData = SeasonData.Load(seasonDataFilePath);

var seriesId = seasonData.SeriesId;
var seasonId = seasonData.SeasonId;

var newEpisodes = new List<Guid>();

foreach (var file in Directory.GetFiles(directoryPath))
{
    if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    Console.WriteLine($"Analyzing {file}...");
    // Console.WriteLine($"Duration: {GetVideoDurationInSeconds(file)}");
    var filename = Path.GetFileNameWithoutExtension(file);
    if (!seasonData.Episodes.ContainsKey(filename))
    {
        seasonData.Episodes[filename] = Guid.NewGuid();
        newEpisodes.Add(seasonData.Episodes[filename]);
    }

    var duration = GetVideoDurationInSeconds(file);
    var fingerprintDuration = Math.Min(duration >= 5 * 60 ? duration * _analysisPercent : duration, 60 * _config.AnalysisLengthLimit);
    var maxCreditsDuration = Math.Min(duration >= 5 * 60 ? duration * _analysisPercent : duration, 60 * _config.MaximumCreditsDuration);

    var (episodeNumber, seasonNumber) = getEpisodeAndSeasonNumber(file);

    analysisQueue.Add(new QueuedEpisode
    {
        SeriesName = getSeriesName(file),
        SeasonNumber = seasonNumber,
        SeriesId = seriesId,
        EpisodeId = seasonData.Episodes[filename],
        SeasonId = seasonId,
        Name = Path.GetFileNameWithoutExtension(file),
        Path = file,
        Duration = Convert.ToInt32(duration),
        IntroFingerprintEnd = Convert.ToInt32(fingerprintDuration),
        CreditsFingerprintStart = Convert.ToInt32(duration - maxCreditsDuration),
    });
}

seasonData.Save(seasonDataFilePath);

var analyzedEpisodes = await analyzer.AnalyzeMediaFiles(analysisQueue, AnalysisMode.Introduction, CancellationToken.None);

foreach (var episodeId in newEpisodes)
{
    var idx = analyzedEpisodes.FindIndex(e => e.EpisodeId == episodeId);
    var episode = analyzedEpisodes[idx];
    // SetIntroOnDb(episode, getLinkId(episode.Path));
}

Console.WriteLine("Analysis complete.");


static double GetVideoDurationInSeconds(string videoPath)
{
    try
    {
        // Create process start info
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffprobe",  // Make sure ffprobe is in your PATH
            Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Start the process
        using (var process = Process.Start(startInfo))
        {
            if (process == null)
                throw new Exception("Failed to start FFprobe process");

            // Read the output
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();

            // Wait for the process to exit
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new Exception($"FFprobe error: {error}");

            // Parse the duration
            if (double.TryParse(output.Trim(), out double duration))
                return duration;

            throw new Exception("Failed to parse video duration");
        }
    }
    catch (Exception ex)
    {
        throw new Exception($"Error getting video duration: {ex.Message}", ex);
    }
}

static void SetIntroOnDb(QueuedEpisode episode, int linkId)
{
    if (episode.IntroStart == 0 && episode.IntroEnd == 0)
    {
        return;
    }

    var request = new HttpRequestMessage(HttpMethod.Post, "https://tv-show-tracker-azure.vercel.app/api/links/set-intro");
    request.Content = new StringContent(JsonSerializer.Serialize(new
    {
        linkId = linkId,
        introStartSeconds = episode.IntroStart,
        introEndSeconds = episode.IntroEnd
    }), Encoding.UTF8, "application/json");

    var client = new HttpClient();
    var response = client.SendAsync(request).Result;

    if (!response.IsSuccessStatusCode)
    {
        throw new Exception("Failed to set intro on database.");
    }
}

static string getSeriesName(string file)
{
    // Example: /Users/username/Downloads/Star Trek Lower Decks/5/S5E6[linkid-123].mp4

    var seriesName = file.Split("/")[^3];
    return seriesName;
}

static (int episodeNumber, int seasonNumber) getEpisodeAndSeasonNumber(string file)
{
    // Example: /Users/username/Downloads/Star Trek Lower Decks/5/S5E6[linkid-123].mp4

    var filename = Path.GetFileNameWithoutExtension(file);
    // Regex to match S5E6
    var match = Regex.Match(filename, @"S(\d+)E(\d+)");
    var seasonNumber = Int32.Parse(match.Groups[1].Value);
    var episodeNumber = Int32.Parse(match.Groups[2].Value);
    return (episodeNumber, seasonNumber);
}

static int getLinkId(string path)
{
    // Example: /Users/username/Downloads/Star Trek Lower Decks/5/S5E6[linkid-123].mp4

    var filename = Path.GetFileNameWithoutExtension(path);
    var linkId = filename.Split("[linkid-")[1].Split("]")[0];
    return Int32.Parse(linkId);
}
