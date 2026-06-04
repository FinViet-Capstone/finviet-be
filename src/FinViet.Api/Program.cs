using Microsoft.EntityFrameworkCore;
using FinViet.Infrastructure.Persistence.Context;
using MediatR;
using FinViet.Application.Interfaces;
using FinViet.Infrastructure.Persistence.Repositories;
using FinViet.Infrastructure.ExternalServices.TransactionImport;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<FinVietDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
builder.Services.AddScoped<IWalletRepository, WalletRepository>();
builder.Services.AddScoped<ITransactionImportRepository, TransactionImportRepository>();
builder.Services.AddScoped<IBankStatementParser, BankStatementExcelParser>();
builder.Services.AddScoped<ISmsTransactionParser, SmsTransactionParser>();

builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(
    typeof(Program).Assembly,
    AppDomain.CurrentDomain.GetAssemblies()
        .FirstOrDefault(a => a.GetName().Name == "FinViet.Application")
));
    
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:3000",
                "http://localhost:5173",
                "https://localhost:3000",
                "https://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddControllers();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("AllowFrontend");

app.MapControllers();

app.Run();