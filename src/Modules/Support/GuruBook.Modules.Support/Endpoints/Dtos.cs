namespace GuruBook.Modules.Support.Endpoints;

/// <summary>One message in a ticket's conversation.</summary>
public sealed record TicketReplyDto(Guid Id, string Body, bool FromSupport, DateTimeOffset At);

/// <summary>A ticket as the teacher who raised it sees it.</summary>
public sealed record TicketDto(
    Guid Id, string Kind, string Details, string? Screen, string Status, bool HasShot,
    DateTimeOffset At, DateTimeOffset? AnsweredAt, TicketReplyDto[] Replies);

/// <summary>The support screen: who to call, when, and everything this teacher has reported.</summary>
public sealed record SupportDto(string? Phone, string? Hours, TicketDto[] Tickets);

/// <summary>A ticket in the Super Admin's inbox, with the teacher who raised it.</summary>
public sealed record AdminTicketDto(
    Guid Id, Guid TeacherId, string TeacherName, string Kind, string Details, string? Screen, string Status, bool HasShot,
    DateTimeOffset At, DateTimeOffset? AnsweredAt, TicketReplyDto[] Replies);

public sealed record SupportSettingsDto(string? Phone, string? Hours);

public sealed record NewTicketRequest(string? Kind, string? Details, string? Screen);

public sealed record NewReplyRequest(string? Body);

public sealed record StatusRequest(string? Status);
