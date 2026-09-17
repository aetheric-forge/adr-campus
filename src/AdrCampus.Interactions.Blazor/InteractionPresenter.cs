using AethericContracts.Interactions;

namespace AdrCampus.Interactions.Blazor;

/// <summary>Generic UI state. All workflow decisions and validation come from the session.</summary>
public sealed class InteractionPresenter(IInteractionSession session)
{
    public InteractionView? View { get; private set; }
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
    public IReadOnlyList<InteractionMessage> Messages { get; private set; } = [];
    public bool IsBusy { get; private set; }
    public bool IsCompleted { get; private set; }

    public Task LoadAsync(CancellationToken cancellationToken = default) => RunAsync(async () =>
        Apply(await session.LoadAsync(cancellationToken)), confirming: false);

    public Task ReviewAsync(CancellationToken cancellationToken = default)
    {
        if (View is not { IsAvailable: true, CanEdit: true } || IsCompleted) return Task.CompletedTask;
        return RunAsync(async () => Apply(await session.PrepareAsync(
            new Dictionary<string, string>(Values), cancellationToken)), confirming: false);
    }

    public Task ConfirmAsync(CancellationToken cancellationToken = default)
    {
        if (View?.Confirmation is not { } confirmation || IsCompleted) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            var result = await session.ConfirmAsync(confirmation.Token, cancellationToken);
            Messages = result.Messages;
            switch (result.Kind)
            {
                case InteractionResultKind.Completed:
                    IsCompleted = true;
                    View = null;
                    Values.Clear();
                    break;
                case InteractionResultKind.Unavailable:
                    View = null;
                    Values.Clear();
                    break;
                case InteractionResultKind.RetryableFailure:
                    View = View! with { CanEdit = false };
                    break;
                default:
                    View = View! with { Confirmation = null, CanEdit = true };
                    break;
            }
        }, confirming: true);
    }

    public void Edit()
    {
        if (IsBusy || IsCompleted || View is not { CanEdit: true }) return;
        View = View with { Confirmation = null };
        Messages = [];
    }

    private void Apply(InteractionView view)
    {
        View = view;
        Messages = view.Messages;
        Values.Clear();
        if (view.IsAvailable)
            foreach (var field in view.Fields) Values[field.Id] = field.Value;
    }

    private async Task RunAsync(Func<Task> operation, bool confirming)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await operation(); }
        catch (Exception)
        {
            // Do not show exception text, and never replace an uncertain confirmation with a new operation.
            Messages = [new(confirming
                ? "The result could not be confirmed. Retry to check the same operation."
                : "This interaction is temporarily unavailable. Try again.")];
            if (confirming && View is not null) View = View with { CanEdit = false };
        }
        finally { IsBusy = false; }
    }
}
