using System.Reflection;
using EnglishTrainer.IntegrationTests.Database;
using EnglishTrainer.TestRunner;

// Точка входа для автономного запуска: в ограниченных окружениях тестовый хост
// VSTest не стартует, а тесты запускать нужно.
//
// Отсутствие базы — не повод молча пропустить прогон. Пропуск, который
// заканчивается успехом, опаснее падения: «все тесты пройдены» на машине,
// где не выполнился ни один, — это ложная уверенность.
if (!TestDatabase.IsConfigured)
{
    Console.WriteLine(TestDatabase.NotConfiguredMessage());
    return 1;
}

return await StandaloneRunner.RunAsync(Assembly.GetExecutingAssembly(), "EnglishTrainer.IntegrationTests");
