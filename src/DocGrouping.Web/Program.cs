using DocGrouping.Application.Interfaces;
using DocGrouping.Infrastructure.Services;
using DocGrouping.Domain.Interfaces;
using DocGrouping.Infrastructure.Persistence;
using DocGrouping.Infrastructure.Persistence.Repositories;
using DocGrouping.Infrastructure.Rules;
using DocGrouping.Infrastructure.TextProcessing;
using DocGrouping.Web.Components;
using DocGrouping.Web.Middleware;
using DocGrouping.Web.Services;
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

	// Database switching services
	builder.Services.AddSingleton<DatabaseSelectorState>();
	builder.Services.AddSingleton<DatabaseConnectionResolver>();
	builder.Services.AddSingleton<DatabaseInitializer>();
	builder.Services.AddHttpContextAccessor();

	// EF Core + PostgreSQL (dynamic connection per active database)
	builder.Services.AddDbContext<DocGroupingDbContext>((sp, options) =>
	{
		var resolver = sp.GetRequiredService<DatabaseConnectionResolver>();
		var httpContextAccessor = sp.GetService<IHttpContextAccessor>();
		var dbState = sp.GetRequiredService<DatabaseSelectorState>();

		var dbName = httpContextAccessor?.HttpContext?.Items["ActiveDatabase"] as string
			?? dbState.ActiveDatabaseName;

		options.UseNpgsql(resolver.GetConnectionString(dbName));
	});

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

	// Ensure all configured databases exist
	var dbInitializer = app.Services.GetRequiredService<DatabaseInitializer>();
	var dbState = app.Services.GetRequiredService<DatabaseSelectorState>();
	foreach (var dbName in dbState.AvailableDatabases)
	{
		await dbInitializer.EnsureCreatedAsync(dbName);
	}

	if (!app.Environment.IsDevelopment())
	{
		app.UseExceptionHandler("/Error", createScopeForErrors: true);
	}

	app.UseSerilogRequestLogging();
	app.UseMiddleware<DatabaseSelectionMiddleware>();
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
