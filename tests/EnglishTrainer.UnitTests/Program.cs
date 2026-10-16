using System.Reflection;
using EnglishTrainer.TestRunner;

// Точка входа нужна только для автономного запуска тестов
// (dotnet run --project tests/EnglishTrainer.UnitTests).
// Штатный способ — dotnet test — использует тот же набор тестов и эту точку входа не задействует.
return await StandaloneRunner.RunAsync(Assembly.GetExecutingAssembly(), "EnglishTrainer.UnitTests");
