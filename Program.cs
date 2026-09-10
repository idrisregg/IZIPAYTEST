using IZIPay.Data;
using IZIPay.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using IZIPay.Repos;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddControllers();
builder.Services.AddCors(options => options.AddPolicy("Front", policy =>
{
    policy
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowAnyOrigin();
}));
var connectionString = builder.Configuration.GetConnectionString("IziPay");



builder.Services.AddDbContext<IziPayDbContext>(options =>
    options.UseNpgsql(connectionString));


builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
builder.Services.AddHttpClient("Gateway");
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddOpenApi();

builder.Services.AddRateLimiter(s =>
{
    s.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    s.AddFixedWindowLimiter(policyName: "Fixed", op =>
    {
        op.PermitLimit = 100;
        op.Window = TimeSpan.FromMinutes(1);
        op.QueueLimit = 0;
        op.AutoReplenishment = true;
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
app.UseRouting();
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors("Front");

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();

app.Run();