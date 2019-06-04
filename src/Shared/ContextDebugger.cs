using System;

namespace Shared
{
    public static class ContextDebugger
    {
        public static Action<object> Write => (s) => Console.Write(s);

        public static void WriteLine() => WriteLine("");

        public static void WriteLine(object s)
        {
            Write(s);
            Write(Environment.NewLine);
        }
    }
}
