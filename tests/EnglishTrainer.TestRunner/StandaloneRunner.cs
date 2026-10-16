using System.Reflection;
using System.Runtime.CompilerServices;

namespace EnglishTrainer.TestRunner;

/// <summary>
/// Автономный запуск тестов без VSTest.
/// </summary>
/// <remarks>
/// <para>
/// Штатный способ запуска — <c>dotnet test</c>, и он остаётся основным.
/// Но тестовый хост VSTest при старте открывает handle родительского процесса,
/// чтобы завершиться вместе с ним. В окружениях, где доступ к процессам ограничен,
/// он падает с «Отказано в доступе», не дойдя ни до одного теста.
/// </para>
/// <para>
/// Этот раннер выполняет те же самые тесты и те же проверки в текущем процессе:
/// <code>dotnet run --project tests/EnglishTrainer.UnitTests</code>
/// Меняется только способ запуска, набор тестов остаётся общим — он лежит
/// в обычных классах с атрибутами xUnit.
/// </para>
/// <para>
/// Раннер живёт в отдельной библиотеке, потому что проектов с тестами два —
/// модульные и интеграционные — и копия этого файла в каждом разошлась бы.
/// </para>
/// </remarks>
public static class StandaloneRunner
{
    /// <summary>Выполняет все тесты сборки. Возвращает код возврата процесса.</summary>
    /// <param name="assembly">Сборка с тестами.</param>
    /// <param name="testNamespace">
    /// Префикс пространства имён, которым ограничивается поиск тестов. Он нужен,
    /// чтобы раннер не подхватывал чужие типы из подключённых сборок.
    /// </param>
    public static async Task<int> RunAsync(Assembly assembly, string testNamespace)
    {
        var passed = 0;
        var failures = new List<string>();

        var testClasses = assembly.GetTypes()
            .Where(type => IsTestClass(type, testNamespace))
            .Select(type => (Type: type, Methods: TestMethods(type).ToList()))
            .Where(entry => entry.Methods.Count > 0)
            .OrderBy(entry => entry.Type.FullName, StringComparer.Ordinal);

        foreach (var (testClass, methods) in testClasses)
        {
            Console.WriteLine();
            Console.WriteLine(testClass.Name);

            foreach (var method in methods)
            {
                foreach (var (displayName, arguments) in EnumerateCases(testClass, method))
                {
                    var outcome = await InvokeAsync(testClass, method, arguments);

                    if (outcome is null)
                    {
                        passed++;
                        Console.WriteLine($"  OK      {displayName}");
                    }
                    else
                    {
                        Console.WriteLine($"  FAIL    {displayName}");
                        Console.WriteLine($"          {outcome.GetType().Name}: {outcome.Message}");
                        failures.Add($"{testClass.FullName}.{displayName}: {outcome}");
                    }
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine(new string('-', 60));

        if (failures.Count == 0)
        {
            Console.WriteLine($"Все тесты пройдены: {passed}");
            return 0;
        }

        Console.WriteLine($"Пройдено: {passed}, провалено: {failures.Count}");
        return 1;
    }

    /// <summary>Выполняет один тест. Возвращает исключение или <c>null</c>, если тест прошёл.</summary>
    private static async Task<Exception?> InvokeAsync(
        Type testClass, MethodInfo method, object?[] arguments)
    {
        try
        {
            // xUnit создаёт новый экземпляр класса на каждый тест: так тесты
            // не зависят от состояния, оставленного соседним тестом.
            var instance = Activator.CreateInstance(testClass);

            var result = method.Invoke(instance, arguments);

            if (result is Task task)
                await task;

            return null;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            return ex.InnerException;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>Разворачивает тест в набор случаев: обычный тест — один, теория — по числу данных.</summary>
    private static IEnumerable<(string DisplayName, object?[] Arguments)> EnumerateCases(
        Type testClass, MethodInfo method)
    {
        if (method.GetCustomAttribute<TheoryAttribute>() is null)
        {
            yield return ($"{testClass.Name}.{method.Name}", []);
            yield break;
        }

        var parameterCount = method.GetParameters().Length;
        var index = 0;

        foreach (var inlineData in method.GetCustomAttributes<InlineDataAttribute>())
        {
            index++;

            foreach (var row in inlineData.GetData(method))
            {
                yield return (
                    $"{testClass.Name}.{method.Name}[{index}]",
                    NormalizeArguments(row, parameterCount));
            }
        }
    }

    /// <summary>
    /// Приводит набор аргументов к числу параметров теста.
    /// </summary>
    /// <remarks>
    /// Нужно для <c>[InlineData(null)]</c>: компилятор передаёт единственный
    /// <c>null</c> как пустой набор аргументов, а тест ожидает один аргумент
    /// со значением <c>null</c>. Такой случай встречается там, где проверяется
    /// отсутствующее значение — пустой ответ, отсутствующий токен.
    /// </remarks>
    private static object?[] NormalizeArguments(object?[]? row, int parameterCount)
    {
        if (row is { Length: > 0 })
            return row;

        var arguments = new object?[parameterCount];
        return arguments;
    }

    private static IEnumerable<MethodInfo> TestMethods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(IsTestMethod)
            .OrderBy(method => method.Name, StringComparer.Ordinal);

    /// <summary>
    /// Класс с тестами. Служебные типы компилятора (замыкания, итераторы)
    /// и вспомогательные классы без тестов пропускаются.
    /// </summary>
    private static bool IsTestClass(Type type, string testNamespace) =>
        type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
        && type.Namespace?.StartsWith(testNamespace, StringComparison.Ordinal) == true
        && !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);

    private static bool IsTestMethod(MethodInfo method) =>
        method.GetCustomAttribute<FactAttribute>() is not null
        || method.GetCustomAttribute<TheoryAttribute>() is not null;
}
