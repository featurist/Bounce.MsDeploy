using System;

namespace Bounce.MsDeploy
{
    public class ConsoleOutput : IOutput
    {
        public void Output(string message)
        {
            Console.WriteLine(message);
        }
    }
}