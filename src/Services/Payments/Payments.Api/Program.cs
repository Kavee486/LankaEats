using System.Security.Claims;
using LankaEats.ServiceDefaults;
using LankaEats.ServiceDefaults.Auth;
using LankaEats.ServiceDefaults.Data;
using LankaEats.ServiceDefaults.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Payments.Api.Data;
using Payments.Api.Features;
using Payments.Api.Gateway;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("payments");

builder.Services.AddDbContext<PaymentsDbContext>(o =>
    o.UseLankaEatsNpgsql(builder.Configuration.GetRequiredConnectionString("payments")));
builder.Services.AddHealthChecks().AddDbContextCheck<PaymentsDbContext>("payments-db");
builder.Services.AddSingleton<IPaymentGateway, SimulatedPaymentGateway>();

builder.AddLankaEatsMessaging<PaymentsDbContext>(x =>
    x.AddConsumer<ProcessPaymentConsumer>().Endpoint(e => e.Name = "payments-stock-reserved"));

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();

app.MapGet("/api/payments/{orderId:guid}", async Task<IResult> (Guid orderId, ClaimsPrincipal user, PaymentsDbContext db, CancellationToken ct) =>
    {
        var payment = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        if (payment is null || (!user.IsAdmin() && payment.CustomerId != user.GetUserId())) return TypedResults.NotFound();

        return TypedResults.Ok(new
        {
            payment.Id,
            payment.OrderId,
            payment.Amount,
            payment.Method,
            Status = payment.Status.ToString(),
            payment.ProviderReference,
            payment.FailureReason,
            payment.CreatedAt,
        });
    })
    .RequireAuthorization()
    .WithTags("Payments")
    .WithName("GetPaymentForOrder");

await app.MigrateDatabaseAsync<PaymentsDbContext>();
await app.RunAsync();

public partial class Program;
