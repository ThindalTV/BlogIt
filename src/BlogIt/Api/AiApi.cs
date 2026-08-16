using BlogIt.Shared;
using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Entities;
using BlogIt.Shared.Helpers;
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
        var errors = new Dictionary<string, string[]>();
        TextFieldValidator.CheckRequired(errors, "title", "Title", req.Title, ContentLimits.TitleLength);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

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
        ILogger<IAiService> logger,
        CancellationToken ct)
    {
        var userId = Guid.Parse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var exists = await db.AiConversations.AnyAsync(c => c.Id == id && c.CreatedByUserId == userId, ct);
        if (!exists) return Results.NotFound();

        try
        {
            var result = await aiService.SendMessageAsync(id, req.Content, ct);
            return Results.Ok(result);
        }
        catch (Exception ex) when (HandleAiFailure(ex, id, logger, out var problem))
        {
            return problem;
        }
    }

    private static async Task<IResult> ExportDraft(
        Guid id,
        ExportAiConversationRequest req,
        BlogItDbContext db,
        IAiService aiService,
        BlogItOptions options,
        ClaimsPrincipal user,
        ILogger<IAiService> logger,
        CancellationToken ct)
    {
        var userId = Guid.Parse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var exists = await db.AiConversations.AnyAsync(c => c.Id == id && c.CreatedByUserId == userId, ct);
        if (!exists) return Results.NotFound();

        try
        {
            var post = await aiService.ExportToDraftAsync(id, userId, req.AdditionalInstructions, ct);
            return Results.Created(
                BlogItPath.Combine(options.ApiPath, $"posts/{post.Id}"),
                new ExportAiConversationResponse(post.Id, post.Slug));
        }
        catch (Exception ex) when (HandleAiFailure(ex, id, logger, out var problem))
        {
            return problem;
        }
    }

    // Runs inside the `when` clause of a catch so it observes the exception without unwinding
    // the stack twice, and logs the real exception server-side while the caller only ever sees
    // a generic message — a missing API key, a provider HTTP error, or the KeyNotFoundException
    // possible if the conversation is deleted between the existence check above and this call
    // would otherwise propagate as an unhandled 500 with internal details attached.
    //
    // The logger category above is the interface, not an implementation. The OpenAI-backed
    // implementation now lives in the BlogIt.OpenAi satellite package, which this assembly must
    // not reference, and ILogger<T> cannot name a static class such as AiApi — so the category is
    // "BlogIt.Services.IAiService", stable across whichever provider package is installed.
    private static bool HandleAiFailure(
        Exception ex,
        Guid conversationId,
        ILogger<IAiService> logger,
        out IResult problem)
    {
        switch (ex)
        {
            case KeyNotFoundException:
                problem = Results.NotFound();
                return true;

            case InvalidOperationException:
                // Reserved for known, safe-to-surface configuration conditions, never a leaked
                // secret or stack trace: no AI package installed at all
                // (NotConfiguredAiService), a missing AI API key, or the provider returning no
                // text. The message is echoed to the caller precisely so the admin's AI screen can
                // tell the operator what to fix.
                logger.LogWarning(ex, "AI request rejected for conversation {ConversationId}", conversationId);
                problem = Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
                return true;

            default:
                logger.LogError(ex, "AI provider request failed for conversation {ConversationId}", conversationId);
                problem = Results.Problem(
                    "The AI request failed. Please try again.",
                    statusCode: StatusCodes.Status502BadGateway);
                return true;
        }
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
