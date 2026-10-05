using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace Oliver4.IconLibrary.Tests
{
    // Minimal test runner with no NuGet dependencies (no xUnit/NUnit), so the suite builds and runs offline.
    // Mark a public instance method with [Test]; each test gets a fresh instance of its class.

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TestAttribute : Attribute
    {
        public string Id { get; }
        public TestAttribute(string id = null) { Id = id; }
    }

    public sealed class AssertionException : Exception
    {
        public AssertionException(string message) : base(message) { }
    }

    public static class Assert
    {
        public static void True(bool condition, string message = null) { if (!condition) throw new AssertionException(message ?? "expected true"); }
        public static void False(bool condition, string message = null) { if (condition) throw new AssertionException(message ?? "expected false"); }
        public static void Null(object o, string message = null) { if (o != null) throw new AssertionException(message ?? $"expected null, got {o}"); }
        public static void NotNull(object o, string message = null) { if (o == null) throw new AssertionException(message ?? "expected a value, got null"); }

        public static void Equal<T>(T expected, T actual, string message = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new AssertionException((message == null ? "" : message + ": ") + $"expected <{expected}> but was <{actual}>");
        }

        public static void NotEqual<T>(T notExpected, T actual, string message = null)
        {
            if (EqualityComparer<T>.Default.Equals(notExpected, actual))
                throw new AssertionException((message == null ? "" : message + ": ") + $"did not expect <{actual}>");
        }

        public static void Contains(string expectedSubstring, string actual, string message = null)
        {
            if (actual == null || actual.IndexOf(expectedSubstring, StringComparison.Ordinal) < 0)
                throw new AssertionException((message == null ? "" : message + ": ") + $"expected to find <{expectedSubstring}> in <{Trim(actual)}>");
        }

        public static void DoesNotContain(string unexpected, string actual, string message = null)
        {
            if (actual != null && actual.IndexOf(unexpected, StringComparison.Ordinal) >= 0)
                throw new AssertionException((message == null ? "" : message + ": ") + $"did not expect <{unexpected}> in <{Trim(actual)}>");
        }

        public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message = null)
        {
            var e = expected.ToList(); var a = actual.ToList();
            if (!e.SequenceEqual(a))
                throw new AssertionException((message == null ? "" : message + ": ") + $"expected [{string.Join(", ", e)}] but was [{string.Join(", ", a)}]");
        }

        public static TEx Throws<TEx>(Action action) where TEx : Exception
        {
            try { action(); }
            catch (TEx ex) { return ex; }
            catch (Exception ex) { throw new AssertionException($"expected {typeof(TEx).Name} but got {ex.GetType().Name}: {ex.Message}"); }
            throw new AssertionException($"expected {typeof(TEx).Name} but nothing was thrown");
        }

        private static string Trim(string s) => s == null ? "null" : (s.Length > 300 ? s.Substring(0, 300) + "…" : s);
    }

    public static class Program
    {
        public static int Main(string[] args)
        {
            var filter = args.FirstOrDefault();
            var tests = typeof(Program).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => (t, m)))
                .Where(x => x.m.GetCustomAttribute<TestAttribute>() != null)
                .OrderBy(x => x.t.Name).ThenBy(x => x.m.MetadataToken)
                .ToList();
            if (!string.IsNullOrEmpty(filter))
                tests = tests.Where(x => (x.t.Name + "." + x.m.Name).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            int pass = 0, fail = 0;
            var failures = new List<string>();
            string current = null;
            foreach (var (type, method) in tests)
            {
                if (type.Name != current) { current = type.Name; Console.WriteLine(); Console.WriteLine(type.Name); }
                var id = method.GetCustomAttribute<TestAttribute>().Id;
                var label = (id != null ? "[" + id + "] " : "") + method.Name;
                var sw = Stopwatch.StartNew();
                try
                {
                    var instance = Activator.CreateInstance(type);
                    method.Invoke(instance, null);
                    pass++;
                    Console.WriteLine($"  PASS  {label} ({sw.ElapsedMilliseconds} ms)");
                }
                catch (TargetInvocationException tie)
                {
                    fail++;
                    var ex = tie.InnerException ?? tie;
                    var msg = ex is AssertionException ? ex.Message : ex.GetType().Name + ": " + ex.Message + (Environment.GetEnvironmentVariable("TEST_TRACE") == "1" ? "\n" + ex : "");
                    Console.WriteLine($"  FAIL  {label}");
                    Console.WriteLine("        " + msg.Replace("\n", "\n        "));
                    failures.Add(type.Name + "." + label + " - " + msg.Split('\n')[0]);
                }
            }
            Console.WriteLine();
            Console.WriteLine($"Host tests: {pass} passed, {fail} failed, {pass + fail} total");
            if (failures.Any())
            {
                Console.WriteLine("Failures:");
                foreach (var f in failures) Console.WriteLine("  - " + f);
            }
            return fail == 0 ? 0 : 1;
        }
    }
}
