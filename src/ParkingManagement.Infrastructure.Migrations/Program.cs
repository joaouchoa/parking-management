using System.Reflection;
using DbUp;

namespace ParkingManagement.Infrastructure.Migrations;

public static class Program
{
    public static int Main(string[] args)
    {
        var connectionString = args.FirstOrDefault()
            ?? Environment.GetEnvironmentVariable("SQLSERVER_CONN")
            ?? throw new InvalidOperationException(
                "Informe a connection string via argumento ou variável de ambiente SQLSERVER_CONN.");

        EnsureDatabase.For.SqlDatabase(connectionString);

        var upgrader = DeployChanges.To
            .SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(result.Error);
            Console.ResetColor();
            return -1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Migrations aplicadas com sucesso.");
        Console.ResetColor();
        return 0;
    }
}
