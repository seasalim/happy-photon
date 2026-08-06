namespace HappyPhoton.Models;

/// <summary>
/// Represents a preset that can be applied to an image's edit settings.
/// </summary>
/// <param name="Id">Unique identifier for the preset</param>
/// <param name="Name">Display name of the preset</param>
/// <param name="Settings">Edit settings values to apply</param>
public record Preset(string Id, string Name, EditSettings Settings);
