namespace WoofBot.Sdk.Models;

public abstract record Event;

public record MessageEvent(Target Target, string SenderId, Messages Messages) : Event;

public record NotifyEvent : Event;

public record CronEvent : Event;