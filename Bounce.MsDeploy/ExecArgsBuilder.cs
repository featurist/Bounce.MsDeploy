using System.Collections.Generic;

namespace Bounce.MsDeploy
{
    internal class ExecArgsBuilder
    {
        private List<string> _args = new List<string>();

        public void Add(string format, params object[] args)
        {
            _args.Add(string.Format(format, args));
        }

        public string Args
        {
            get { return string.Join(" ", _args); }
        }
    }
}