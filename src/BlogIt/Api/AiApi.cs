using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Entities;
using BlogIt.Services;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BlogIt.Api;

public static class AiApi
{
    public static IEndpointRouteBuilder MapAiApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/ai")
            .WithTags("AI")
            .RequireAuthorization(BlogItDefaults.AdminAuthorizationPolicy);

        group.MapGet("/conversations", GetConversations);
        group.MapGet("/conversations/{id:guid}", GetConversation);
        group.MapPost("/conversations", CreateConversation);
        group.MapPost("/conversations/{id:guid}/messages", SendMessage);
        group.MapPost("/conversations/{id:guid}/export-draft", ExportDraft);
        group.MapDelete("/conversations/{id:guid}", DeleteConversation);

        return app;
    }

    private static async Task<IResult> GetConversations(
        BlogItDbContext db,
        ClaimsPrincipal user)
    {
        var userId = Guid.Parse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var conversations = await db.AiConversations
            .Where(c => c.CreatedByUserId == userId)
            .Include(c => c.Messages)
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync();

        return Results.Ok(conversations.Select(ToSummaryDto).ToList());
    }

    private static async Task<IResult> GetConversation(Guid id, BlogItDbContext db, ClaimsPrincipal user)
    {
        var userId = Guid.Parse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var conv = await db.AiConversations
            .Include(c => c.Messages.OrderBy(m => m.CreatedAt))
            .FirstOrDefaultAsync(c => c.Id == id && c.CreatedByUserId == userId);

        return conv is null ? Results.NotFound() : Results.Ok(ToDetailDto(conv));
    }

    private static async Task<IResult> CreateConversation(
        CreateAiConversationRequest req,
        BlogItDbContext db,
        BlogItOptions options,
        ClaimsPrincipal user)
    {
        var userId = Guid.Parse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var conv = new AiConversation
        {
            Title = req.Title,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        db.AiConversations.Add(conv);
        await db.SaveChangesAsync();
        return Results.Created(
            BlogItPath.Combine(options.ApiPath, $"ai/conversations/{conv.Id}"),
            ToDetailDto(conv));
    }

    private static async Task<IResult> SendMessage(
        Guid id,
        SendAiMessageRequest req,
        BlogItDbContext db,
        IAiService aiService,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var userId = Guid.Parse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var exists = await db.AiConversations.AnyAsync(c => c.Id == id && c.CreatedByUserId == userId, ct);
        if (!exists) return Results.NotFound();

        var result = await aiService.SendMessageAsync(id, req.Content, ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> ExportDraft(
        Guid id,
        ExportAiConversationRequest req,
        BlogItDbContext db,
        IAiService aiService,
        BlogItOptions options,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var userId = Guid.Parse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var exists = await db.AiConversations.AnyAsync(c => c.Id == id && c.CreatedByUserId == userId, ct);
        if (!exists) return Results.NotFound();

        var post = await aiService.ExportToDraftAsync(id, userId, req.AdditionalInstructions, ct);
        return Results.Created(
            BlogItPath.Combine(options.ApiPath, $"posts/{post.Id}"),
            new ExportAiConversationResponse(post.Id, post.Slug));
    }

    private static async Task<IResult> DeleteConversation(
        Guid id,
        BlogItDbContext db,
        ClaimsPrincipal user)
    {
        var userId = Guid.Parse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var conv = await db.AiConversations
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == id && c.CreatedByUserId == userId);

        if (conv is null) return Results.NotFound();

        db.AiMessages.RemoveRange(conv.Messages);
        db.AiConversations.Remove(conv);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static AiConversationSummaryDto ToSummaryDto(AiConversation c) => new(
        c.Id, c.Title, c.CreatedAt, c.UpdatedAt,
        c.Messages.Count, c.LinkedDraftId
    );

    private static AiConversationDetailDto ToDetailDto(AiConversation c) => new(
        c.Id, c.Title, c.CreatedAt, c.UpdatedAt, c.LinkedDraftId,
        c.Messages.OrderBy(m => m.CreatedAt)
                  .Select(m => new AiMessageDto(m.Id, m.Role, m.Content, m.CreatedAt))
                  .ToList()
    );
}
