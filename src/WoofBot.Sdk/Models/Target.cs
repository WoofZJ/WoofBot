namespace WoofBot.Sdk.Models;

public enum TargetType
{
    Private,
    Group,
}

public record Target(TargetType Type, string Id);