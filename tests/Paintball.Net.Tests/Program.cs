using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Paintball.Net.Tests
{
    /// <summary>
    /// Abhängigkeitsfreier Testlauf für den Web-MVP-Server (QA-01/QA-02).
    /// Exit-Code 0 = alle Tests bestanden, 1 = mindestens ein Fehler.
    /// Filter: dotnet run --project tests/Paintball.Net.Tests -- Teilname
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var runner = new TestRunner(args.Length > 0 ? args[0] : null);
            Console.WriteLine("=== Paintball.Net Tests (QA-01/QA-02) ===");

            MovementTests.Register(runner);
            MatchTests.Register(runner);
            BotTests.Register(runner);
            AccountTests.Register(runner);
            ServerTests.Register(runner);
            IntegrationTests.Register(runner);
            GoldenTests.Register(runner);

            Console.WriteLine();
            Console.WriteLine($"=== Ergebnis: {runner.Passed} bestanden, {runner.Failed} fehlgeschlagen ===");
            return runner.Failed == 0 ? 0 : 1;
        }
    }

    internal sealed class TestRunner
    {
        private readonly string _filter;
        public int Passed { get; private set; }
        public int Failed { get; private set; }

        public TestRunner(string filter) { _filter = filter; }

        public void Run(string name, Action test) => RunAsync(name, () => { test(); return Task.CompletedTask; });

        public void RunAsync(string name, Func<Task> test)
        {
            if (_filter != null && name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0) return;
            try
            {
                if (!test().Wait(TimeSpan.FromSeconds(30)))
                    throw new TimeoutException("Test hat das Zeitlimit überschritten");
                Passed++;
                Console.WriteLine($"[PASS] {name}");
            }
            catch (Exception ex)
            {
                Failed++;
                Exception inner = ex is AggregateException agg && agg.InnerException != null ? agg.InnerException : ex;
                Console.WriteLine($"[FAIL] {name}: {inner.GetType().Name}: {inner.Message}");
                if (Environment.GetEnvironmentVariable("PB_TEST_STACK") == "1") Console.WriteLine(inner.StackTrace);
            }
        }
    }

    internal static class Assert
    {
        public static void IsTrue(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        public static void IsFalse(bool condition, string message) => IsTrue(!condition, message);

        public static void AreEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"{message} (erwartet: {expected}, tatsächlich: {actual})");
        }

        public static void AreClose(float expected, float actual, float tolerance, string message)
        {
            if (MathF.Abs(expected - actual) > tolerance)
                throw new Exception($"{message} (erwartet: {expected}±{tolerance}, tatsächlich: {actual})");
        }
    }
}
