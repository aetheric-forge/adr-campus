using AdrCampus.Application.Identity;
using AdrCampus.Core.Domain;
using AdrCampus.Core.Drafts;
using AdrCampus.Core.Proposals;
using AethericForge.Runtime.Abstractions.Interfaces.Authorities;
using AethericForge.Runtime.Abstractions.Interfaces.Institutions;
using AethericForge.Runtime.Abstractions.Interfaces.Knowledge.Providers;
using AethericForge.Runtime.Abstractions.Interfaces.Knowledge.Services;
using AethericForge.Runtime.Abstractions.Interfaces.Library.Services;
using AethericForge.Runtime.Abstractions.Interfaces.Workbench.Services;
using AethericForge.Runtime.Institutions.Abstractions.Builders;
using AethericForge.Runtime.Institutions.Decisions;
using AethericForge.Runtime.Institutions.Library;
using AethericForge.Runtime.Institutions.Workbench;
using AethericForge.Runtime.Models.Authorities;
using AethericForge.Runtime.Models.Institutions;
using AethericForge.Runtime.Providers.Knowledge.InMemory;
using AethericForge.Runtime.Providers.Staging.InMemory;
using AethericForge.Runtime.Services.Knowledge;
using AethericForge.Runtime.Services.Library;
using AethericForge.Runtime.Services.Staging;
using AethericForge.Runtime.Services.Workbench;
using Microsoft.Extensions.DependencyInjection;

namespace AdrCampus.Plugin.Tests;

// A host with no Campus or Web reference and no registration of ADR implementation services.
internal sealed class ReviewHost : IDisposable
{
    public static readonly OrganizationId Organization = new("review-host");
    public static readonly MemberId Maintainer = new("maintainer");
    public static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    public ServiceProvider Services { get; }
    public IInstitution Owner => Services.GetRequiredService<IInstitution>();
    public IDecisions Office => Owner.ResolveOrganization<IDecisions>("decisions");
    public Authority Membership { get; } = new();

    public ReviewHost(bool includeLibrary = true, bool includeWorkbench = true, bool mount = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IKnowledgeProvider>(new InMemoryKnowledgeProvider("adr-campus"));
        services.AddSingleton<ITeam<ICuratorClerk>>(new Team<ICuratorClerk>([]));
        services.AddSingleton<IKnowledgeService, KnowledgeService>();
        services.AddSingleton<ITeam<ILibraryClerk>>(new Team<ILibraryClerk>([]));
        services.AddSingleton<ILibrarian, Librarian>();
        services.AddSingleton<ICurator, Curator>();
        services.AddSingleton<TimeProvider>(new Clock());
        services.AddScoped<IMemberAuthority>(_ => Membership);
        services.AddScoped<Caller>();
        services.AddScoped<IProposalReviewCaller>(sp => sp.GetRequiredService<Caller>());
        services.AddDecisionsOffice(Organization, sp => sp.GetRequiredService<IInstitution>());
        services.AddSingleton<IInstitution>(sp =>
        {
            var template = InstitutionTemplateBuilder.Create()
                .WithDescriptor("Host", new Version(1, 0, 0), "Independent package test host").Build();
            var owner = new TestInstitution(new InstitutionContext(template, sp));
            if (includeLibrary)
                owner.Register<ILibrary>(ActivatorUtilities.CreateInstance<Library>(sp,
                    new LibraryContext(template, sp, owner)));
            if (includeWorkbench)
            {
                var artificer = new Artificer(new StagingService([new InMemoryStagingProvider("adr-campus-workbench")]),
                    new Team<IWorkbenchWorker>([]));
                owner.Register<IWorkbench>(new Workbench(new WorkbenchContext(template, sp, owner),
                    artificer, new WorkbenchService()));
            }
            if (mount)
            {
                var factory = new DecisionsOrganizationFactory();
                owner.RegisterOrganization(factory.OrganizationId, factory.Create(owner, sp));
            }
            return owner;
        });
        Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public async Task<AdrProposal> SeedAsync(OrganizationId? organization = null)
    {
        var id = organization ?? Organization;
        var draft = AdrDraft.Create(AdrId.New(), id, Maintainer,
            new DraftContent("Choose PostgreSQL", "Context", "Decision", "Consequences"), Now);
        await Services.GetRequiredService<IDraftRepository>().CreateAsync(draft, OperationId.New());
        return (await Services.GetRequiredService<IProposalRepository>().ProposeAsync(id, Maintainer,
            draft.Id, draft.Version, OperationId.New(), Now)).Proposal!;
    }

    public void Dispose() => Services.Dispose();
    private sealed class TestInstitution(IInstitutionContext context) : InstitutionBase(context);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now.AddMinutes(1); }

    public sealed class Caller : IProposalReviewCaller
    {
        public MemberId? Member { get; set; } = Maintainer;
        public Task<MemberId?> GetMemberIdAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Member);
        }
    }

    public sealed class Authority : IMemberAuthority
    {
        public bool Enabled { get; set; } = true;
        public Task<bool> IsActiveMemberAsync(OrganizationId organizationId, MemberId memberId,
            CancellationToken cancellationToken = default) => Task.FromResult(organizationId == Organization);
        public Task<bool> IsActiveMaintainerAsync(OrganizationId organizationId, MemberId memberId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Enabled && organizationId == Organization && memberId == Maintainer);
    }
}
