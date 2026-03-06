using DocGrouping.Application.Interfaces;
using DocGrouping.Infrastructure.Configuration;
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

		var logger = sp.GetService<ILogger<DocGroupingDbContext>>();
		logger?.LogInformation("DbContext factory resolving database: '{Database}' (from HttpContext: {FromHttp})",
			dbName, httpContextAccessor?.HttpContext?.Items["ActiveDatabase"] != null);

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

	// Grouping thresholds
	builder.Services.Configure<GroupingThresholds>(builder.Configuration.GetSection("GroupingThresholds"));

	// PDF storage
	builder.Services.Configure<PdfStorageOptions>(builder.Configuration.GetSection("PdfStorage"));
	builder.Services.AddSingleton<IPdfStorageService, PdfStorageService>();

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

	// Default to the database with the fewest records
	{
		var resolver = app.Services.GetRequiredService<DatabaseConnectionResolver>();
		string? smallestDb = null;
		long smallestCount = long.MaxValue;
		foreach (var dbName in dbState.AvailableDatabases)
		{
			try
			{
				var optionsBuilder = new DbContextOptionsBuilder<DocGroupingDbContext>();
				optionsBuilder.UseNpgsql(resolver.GetConnectionString(dbName));
				await using var ctx = new DocGroupingDbContext(optionsBuilder.Options);
				var count = await ctx.Documents.LongCountAsync();
				Log.Information("Database '{Database}' has {Count} documents", dbName, count);
				if (count < smallestCount)
				{
					smallestCount = count;
					smallestDb = dbName;
				}
			}
			catch (Exception ex)
			{
				Log.Warning(ex, "Could not count documents in '{Database}'", dbName);
			}
		}
		if (smallestDb != null && smallestDb != dbState.ActiveDatabaseName)
		{
			dbState.ActiveDatabaseName = smallestDb;
			Log.Information("Defaulting to database '{Database}' ({Count} documents)", smallestDb, smallestCount);
		}
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
