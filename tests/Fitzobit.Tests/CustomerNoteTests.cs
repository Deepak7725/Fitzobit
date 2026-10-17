using System.Text.Json;
using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Common;
using Fitzobit.Application.Customers;
using Fitzobit.Domain.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Tests;

/// <summary>Dated customer notes: anyone adds; the writer or an Admin edits/deletes; every change is audited.</summary>
public class CustomerNoteTests
{
    private sealed class OtherUser(string role) : ICurrentUser
    {
        public string UserName => "other-user";
        public string DisplayName => "Other User";
        public string Role => role;
    }

    private static CustomerNoteService NotesAs(TestDatabase test, Infrastructure.Persistence.FitzobitDbContext db, ICurrentUser user) =>
        new(db, new AuditLogger(db, user, test.Clock), user);

    [Fact]
    public async Task Notes_are_listed_newest_first_with_writer_and_date()
    {
        using var test = new TestDatabase();
        int id;
        await using (var db = test.CreateContext()) id = (await test.Commands(db).CreateAsync(test.NewCustomer(note: "Gate code 1234"))).Id;
        test.Clock.UtcNow = test.Clock.UtcNow.AddDays(2);
        await using (var db = test.CreateContext()) await test.Notes(db).AddAsync(id, new SaveNoteRequest(" Less spice from next week. "));

        await using var check = test.CreateContext();
        var notes = (await test.Queries(check).GetDetailAsync(id)).Notes;
        Assert.Equal(["Less spice from next week.", "Gate code 1234"], notes.Select(n => n.Text));
        Assert.All(notes, n => Assert.Equal(("test-user", true, false), (n.CreatedBy, n.CanEdit, n.Edited)));
        Assert.Equal(test.Clock.UtcNow, notes[0].CreatedAt);

        await Assert.ThrowsAsync<BusinessRuleException>(() => test.Notes(check).AddAsync(id, new SaveNoteRequest("   ")));
        await Assert.ThrowsAsync<NotFoundException>(() => test.Notes(check).AddAsync(999_999, new SaveNoteRequest("Hello")));
    }

    [Fact]
    public async Task The_writer_edits_and_deletes_own_notes_and_changes_are_audited()
    {
        using var test = new TestDatabase();
        int id, noteId;
        await using (var db = test.CreateContext())
        {
            id = (await test.Commands(db).CreateAsync(test.NewCustomer())).Id;
            noteId = await test.Notes(db).AddAsync(id, new SaveNoteRequest("Call before delivery"));
        }
        test.Clock.UtcNow = test.Clock.UtcNow.AddHours(1);

        await using var check = test.CreateContext();
        await test.Notes(check).UpdateAsync(noteId, new SaveNoteRequest("Call 10 minutes before delivery"));
        var edited = (await test.Queries(check).GetDetailAsync(id)).Notes.Single();
        Assert.Equal(("Call 10 minutes before delivery", true), (edited.Text, edited.Edited));
        var audit = await check.AuditLogs.SingleAsync(a => a.Action == AuditActions.CustomerNoteUpdated);
        Assert.Equal("Call before delivery", JsonDocument.Parse(audit.OldValues!).RootElement.GetProperty("Note").GetString());

        await test.Notes(check).DeleteAsync(noteId);
        Assert.Empty((await test.Queries(check).GetDetailAsync(id)).Notes);
        Assert.Equal(1, await check.AuditLogs.CountAsync(a => a.Action == AuditActions.CustomerNoteDeleted));
    }

    [Fact]
    public async Task Other_managers_cannot_change_a_note_but_admins_can()
    {
        using var test = new TestDatabase();
        int noteId;
        await using (var db = test.CreateContext())
        {
            var id = (await test.Commands(db).CreateAsync(test.NewCustomer())).Id;
            noteId = await test.Notes(db).AddAsync(id, new SaveNoteRequest("Written by test-user"));
        }

        await using var check = test.CreateContext();
        var manager = NotesAs(test, check, new OtherUser("Manager"));
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => manager.UpdateAsync(noteId, new SaveNoteRequest("Changed by someone else")));
        Assert.Contains("Only the person who wrote this note", error.Message);
        await Assert.ThrowsAsync<BusinessRuleException>(() => manager.DeleteAsync(noteId));

        var admin = NotesAs(test, check, new OtherUser("Admin"));
        await admin.UpdateAsync(noteId, new SaveNoteRequest("Corrected by an admin"));
        Assert.Equal("Corrected by an admin", (await check.CustomerNotes.AsNoTracking().SingleAsync()).Text);
        await admin.DeleteAsync(noteId);
        Assert.Equal(0, await check.CustomerNotes.CountAsync());
    }
}
