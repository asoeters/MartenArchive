// See https://aka.ms/new-console-template for more information

using JasperFx.Core;
using Marten;
using Marten.Events;
using Marten.Events.Daemon.Resiliency;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMarten(options =>
{
    // Establish the connection string to your Marten database
    options.Connection("Server=127.0.0.1;Port=5432;Userid=allard;Password=password;Database=test");

    // Specify that we want to use STJ as our serializer
    options.UseSystemTextJsonForSerialization();
    options.Projections.Snapshot<ValueDto>(Marten.Events.Projections.SnapshotLifecycle.Async);
}).AddAsyncDaemon(DaemonMode.HotCold);
var app = builder.Build();
app.MapGet("/", async (IDocumentStore store) =>
{
    Guid id;
    await using (var session = store.LightweightSession())
    {
        id = session.Events.StartStream<ValueDto>(new Add("test"), new Edit("edit")).Id;
        session.SaveChanges();
    }

    await store.WaitForNonStaleProjectionDataAsync(20.Seconds());

    var stateResultBefore = "";
    await using (var session = store.QuerySession())
    {
        var state = session.Query<ValueDto>().FirstOrDefault(x => x.Id == id);
        stateResultBefore = $"state should not be null state value is {state?.Value}";
    }

    await using (var session = store.LightweightSession())
    {
        session.Events.ArchiveStream(id);
    }

    await store.WaitForNonStaleProjectionDataAsync(20.Seconds());

    string stateResultAfter = "";
    await using (var session = store.QuerySession())
    {
        var state = session.Query<ValueDto>().FirstOrDefault(x => x.Id == id);
        stateResultAfter = ($"state should be null but state value is: {state?.Value}");
    }

    return Results.Ok<List<string>>([stateResultBefore, stateResultAfter]);
});
app.Run();


public record Add(string Value);

public record Edit(string Value);

public class ValueDto
{
    public string Value { get; set; }
    public Guid Id { get; set; }

    public void Apply(Add e)
    {
        Value = e.Value;
    }

    public void Apply(Edit e)
    {
        Value = e.Value;
    }
}