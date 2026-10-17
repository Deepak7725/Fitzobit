using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Common;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.Customers;

public sealed record SaveNoteRequest(string? Text);

/// <param name="Edited">The note was changed after it was written.</param>
/// <param name="CanEdit">The signed-in user may edit or delete it (the writer, or an Admin).</param>
public sealed record CustomerNoteDto(int Id, string Text, DateTime CreatedAt, string? CreatedBy, string? CreatedByName, DateTime UpdatedAt,
    string? UpdatedBy, bool Edited, bool CanEdit);

/// <summary>
/// Dated notes about a customer. Anyone who can work with customers adds notes; a note can be edited or deleted by the user who
/// wrote it or by an Admin. Every change is audited with the old and new text.
/// </summary>
public sealed class CustomerNoteService(IFitzobitDbContext db, IAuditLogger audit, ICurrentUser currentUser)
{
    public async Task<int> AddAsync(int customerId, SaveNoteRequest request, CancellationToken ct = default)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct)
                       ?? throw new NotFoundException($"Customer {customerId} was not found.");
        var text = ValidateText(request.Text, required: true)!;
        var note = new CustomerNote { CustomerId = customer.Id, Text = text };
        db.CustomerNotes.Add(note);
        audit.RecordChange(AuditActions.CustomerNoteAdded, nameof(Customer), customer.CustomerId, $"Note added: {customer.CustomerId}",
            new Dictionary<string, object?>(), new Dictionary<string, object?> { ["Note"] = text });
        await db.SaveChangesAsync(ct);
        return note.Id;
    }

    public async Task UpdateAsync(int noteId, SaveNoteRequest request, CancellationToken ct = default)
    {
        var note = await FindEditableAsync(noteId, ct);
        var text = ValidateText(request.Text, required: true)!;
        if (text == note.Text) return;
        audit.RecordChange(AuditActions.CustomerNoteUpdated, nameof(Customer), note.Customer!.CustomerId, $"Note edited: {note.Customer.CustomerId}",
            new Dictionary<string, object?> { ["Note"] = note.Text }, new Dictionary<string, object?> { ["Note"] = text });
        note.Text = text;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int noteId, CancellationToken ct = default)
    {
        var note = await FindEditableAsync(noteId, ct);
        db.CustomerNotes.Remove(note);
        audit.RecordChange(AuditActions.CustomerNoteDeleted, nameof(Customer), note.Customer!.CustomerId, $"Note deleted: {note.Customer.CustomerId}",
            new Dictionary<string, object?> { ["Note"] = note.Text, ["WrittenBy"] = note.CreatedBy }, new Dictionary<string, object?>());
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The writer of a note, or an Admin, may change it.</summary>
    public static bool CanEdit(string? writer, ICurrentUser user) =>
        user.Role == nameof(UserRole.Admin) || (writer is not null && string.Equals(writer, user.UserName, StringComparison.OrdinalIgnoreCase));

    /// <returns>The trimmed text, or null when empty and not required.</returns>
    public static string? ValidateText(string? text, bool required)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return required ? throw new BusinessRuleException("The note is empty.") : null;
        if (trimmed.Length > CustomerNote.MaxLength) throw new BusinessRuleException($"A note can have at most {CustomerNote.MaxLength:N0} characters.");
        return trimmed;
    }

    private async Task<CustomerNote> FindEditableAsync(int noteId, CancellationToken ct)
    {
        var note = await db.CustomerNotes.Include(n => n.Customer).FirstOrDefaultAsync(n => n.Id == noteId, ct)
                   ?? throw new NotFoundException($"Note {noteId} was not found.");
        if (!CanEdit(note.CreatedBy, currentUser))
            throw new BusinessRuleException("Only the person who wrote this note, or an administrator, can change or delete it.");
        return note;
    }
}
