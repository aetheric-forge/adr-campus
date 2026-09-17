using AethericContracts.Interactions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AdrCampus.Interactions.Blazor.Tests;

public sealed class InteractionPresenterTests
{
    [Fact]
    public async Task RendererUsesPackageLabelsRequiredFieldsAndEncodedContent()
    {
        var presenter = new InteractionPresenter(new Session());
        await presenter.LoadAsync();
        var html = await Render(presenter);
        Assert.Contains("Approve booking", html);
        Assert.Contains("Operator explanation", html);
        Assert.Contains("aria-required=\"true\"", html);
        Assert.Contains("maxlength=\"70\"", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public async Task GenericRendererDisplaysConfirmationFromSession()
    {
        var presenter = new InteractionPresenter(new Session());
        await presenter.LoadAsync();
        presenter.Values["explanation"] = "Because";
        await presenter.ReviewAsync();
        var html = await Render(presenter);
        Assert.Contains("Approve this booking permanently?", html);
        Assert.Contains("Confirm booking", html);
        Assert.Contains("Because", html);
        Assert.DoesNotContain("<textarea", html);
        await presenter.ConfirmAsync();
        Assert.True(presenter.IsCompleted);
    }

    [Fact]
    public async Task ServerValidationIsRenderedWithoutFeatureSpecificHandling()
    {
        var session = new Session { Invalid = true };
        var presenter = new InteractionPresenter(session);
        await presenter.LoadAsync();
        await presenter.ReviewAsync();
        var html = await Render(presenter);
        Assert.Contains("Operator explanation is required", html);
        Assert.Contains("aria-invalid=\"true\"", html);
        Assert.Null(presenter.View!.Confirmation);
        await presenter.ConfirmAsync();
        Assert.Empty(session.ConfirmedTokens);
    }

    [Fact]
    public async Task UncertainConfirmKeepsTokenAndPreventsEditing()
    {
        var session = new Session { LoseFirstReply = true };
        var presenter = new InteractionPresenter(session);
        await presenter.LoadAsync();
        await presenter.ReviewAsync();
        var token = presenter.View!.Confirmation!.Token;
        await presenter.ConfirmAsync();
        Assert.False(presenter.View!.CanEdit);
        presenter.Edit();
        Assert.Equal(token, presenter.View.Confirmation!.Token);
        await presenter.ReviewAsync();
        Assert.Equal(1, session.PrepareCount);
        await presenter.ConfirmAsync();
        Assert.True(presenter.IsCompleted);
        Assert.Equal(new[] { token, token }, session.ConfirmedTokens);
    }

    [Fact]
    public async Task UnavailableResultRemovesProtectedData()
    {
        var session = new Session { Result = InteractionResultKind.Unavailable };
        var presenter = new InteractionPresenter(session);
        await presenter.LoadAsync();
        await presenter.ReviewAsync();
        await presenter.ConfirmAsync();
        Assert.Null(presenter.View);
        Assert.Empty(presenter.Values);
        var html = await Render(presenter);
        Assert.DoesNotContain("private booking", html);
        Assert.Contains("Access unavailable", html);
    }

    [Fact]
    public async Task ConflictMessageComesFromSessionAndAllowsAnotherReview()
    {
        var presenter = new InteractionPresenter(new Session { Result = InteractionResultKind.Conflict });
        await presenter.LoadAsync();
        await presenter.ReviewAsync();
        await presenter.ConfirmAsync();
        Assert.Null(presenter.View!.Confirmation);
        Assert.True(presenter.View.CanEdit);
        Assert.Contains("Booking already changed", await Render(presenter));
    }

    [Fact]
    public async Task ActionRendererUsesProviderLabelsAndDisabledReasons()
    {
        var html = await RenderComponent<InteractionActions>(new Dictionary<string, object?>
        {
            ["Actions"] = new InteractionAction[]
            {
                new("approve-booking", "Reserve room"),
                new("cancel-booking", "Cancel reservation", false, "Already checked in")
            },
            ["ActionUrl"] = (Func<string, string>)(id => "/booking/" + id)
        });
        Assert.Contains("Reserve room", html);
        Assert.Contains("/booking/approve-booking", html);
        Assert.Contains("Cancel reservation", html);
        Assert.Contains("Already checked in", html);
        Assert.DoesNotContain("/booking/cancel-booking", html);
        Assert.Contains("disabled", html);
    }

    private static Task<string> Render(InteractionPresenter presenter) =>
        RenderComponent<InteractionPanel>(new Dictionary<string, object?> { ["Presenter"] = presenter });

    private static async Task<string> RenderComponent<T>(Dictionary<string, object?> parameters) where T : IComponent
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters));
            return component.ToHtmlString();
        });
    }

    private sealed class Session : IInteractionSession
    {
        public bool Invalid { get; init; }
        public bool LoseFirstReply { get; init; }
        public int PrepareCount { get; private set; }
        public List<string> ConfirmedTokens { get; } = [];
        public InteractionResultKind Result { get; init; } = InteractionResultKind.Completed;
        public Task<InteractionView> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(View());
        public Task<InteractionView> PrepareAsync(IReadOnlyDictionary<string, string> inputs, CancellationToken cancellationToken = default)
        {
            PrepareCount++;
            return Task.FromResult(Invalid
                ? View() with { Messages = [new("Operator explanation is required", "explanation")] }
                : View() with { Confirmation = new("opaque-token", "Approve this booking permanently?", "Confirm booking",
                    [new("Explanation", inputs.GetValueOrDefault("explanation") ?? "")]) });
        }
        public Task<InteractionResult> ConfirmAsync(string token, CancellationToken cancellationToken = default)
        {
            ConfirmedTokens.Add(token);
            if (LoseFirstReply && ConfirmedTokens.Count == 1) throw new IOException("Lost reply");
            return Task.FromResult(new InteractionResult(Result, [new(Result switch
            {
                InteractionResultKind.Unavailable => "Access unavailable",
                InteractionResultKind.Conflict => "Booking already changed",
                _ => "Booking approved"
            })]));
        }
        private static InteractionView View() => new("Approve booking", "Review private booking",
            [new("Details", "<script>alert(1)</script>")],
            [new("explanation", "Operator explanation", "Required", true, 70)], "Review booking", []);
    }
}
