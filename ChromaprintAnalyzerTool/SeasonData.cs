using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Jellyfin.Database.Implementations.Entities.Libraries;

public class SeasonData
{
    public Guid SeasonId { get; set; }
    public Guid SeriesId { get; set; }
    public Dictionary<string, Guid> Episodes { get; set; } = new Dictionary<string, Guid>();

    public static SeasonData Load(string filePath)
    {
        if (File.Exists(filePath))
        {
            var json = File.ReadAllBytes(filePath);
            return JsonSerializer.Deserialize<SeasonData>(json);
        }
        return new SeasonData { SeasonId = Guid.NewGuid(), SeriesId = Guid.NewGuid() };
    }

    public void Save(string filePath)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
    }
}
