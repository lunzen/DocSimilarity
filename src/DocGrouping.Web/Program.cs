using DocGrouping.Application.Interfaces;
using DocGrouping.Infrastructure.Services;
using DocGrouping.Domain.Interfaces;
using DocGrouping.Infrastructure.Persistence;
using DocGrouping.Infrastructure.Persistence.Repositories;
using DocGrouping.Infrastructure.Rules;
using DocGrouping.Infrastructure.TextProcessing;
using DocGrouping.Web.Components;
using Microsoft.EntityFrameworkCore;
using Serilog;

Log.Logger = new LoggerConfiguration()
	.WriteTo.Console()
	.CreateBootstrapLogger();

try
{
	var builder = WebApplication.CreateBuilder(args);

	// Serilog
	builder.Host.UseSerilog((context, services, configuration) => configuration
		.ReadFrom.Configuration(context.Configuration)
		.ReadFrom.Services(services));

	// EF Core + PostgreSQL
	builder.Services.AddDbContext<DocGroupingDbContext>(options =>
		options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

	// Repositories
	builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();
	builder.Services.AddScoped<IDocumentGroupRepository, DocumentGroupRepository>();
	builder.Services.AddScoped<IBusinessRuleRepository, BusinessRuleRepository>();
	builder.Services.AddScoped<IProcessingJobRepository, ProcessingJobRepository>();

	// Infrastructure services (singletons - stateless)
	builder.Services.AddSingleton<TextNormalizer>();
	builder.Services.AddSingleton<DocumentFingerprinter>();
	builder.Services.AddSingleton<PdfTextExtractor>();
	builder.Services.AddSingleton<RulesEngine>();
	builder.Services.AddSingleton<OcrErrorSimulator>();
	builder.Services.AddSingleton<StampGenerator>();
	builder.Services.AddSingleton<RedactionSimulator>();

	// Application services
	builder.Services.AddScoped<IDocumentIngestionService, DocumentIngestionService>();
	builder.Services.AddScoped<IGroupingOrchestrator, GroupingOrchestrator>();
	builder.Services.AddScoped<IDocumentGeneratorService, DocumentGeneratorService>();

	// Blazor + API
	builder.Services.AddRazorComponents()
		.AddInteractiveServerComponents();
	builder.Services.AddControllers();

	var app = builder.Build();

	// Apply migrations on startup
	using (var scope = app.Services.CreateScope())
	{
		var db = scope.ServiceProvider.GetRequiredService<DocGroupingDbContext>();
		await db.Database.EnsureCreatedAsync();
	}

	if (!app.Environment.IsDevelopment())
	{
		app.UseExceptionHandler("/Error", createScopeForErrors: true);
	}

	app.UseSerilogRequestLogging();
	app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
	app.UseAntiforgery();

	app.MapStaticAssets();
	app.MapControllers();
	app.MapRazorComponents<App>()
		.AddInteractiveServerRenderMode();

	Log.Information("DocGrouping starting on {Urls}", string.Join(", ", app.Urls));
	await app.RunAsync();
}
catch (Exception ex)
{
	Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
	await Log.CloseAndFlushAsync();
}
