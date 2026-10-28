using System.Collections.Generic;
using System.Text;

namespace TrackKit
{
    /// <summary>Errors, warnings and notes collected by a TrackKit step. Ok means no errors.</summary>
    public sealed class TrackReport
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Info = new List<string>();
        public bool Ok { get { return Errors.Count == 0; } }
        public void Error(string s) { Errors.Add(s); }
        public void Warn(string s) { Warnings.Add(s); }
        public void Note(string s) { Info.Add(s); }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Ok ? "RESULT: PASS" : "RESULT: FAIL (" + Errors.Count + " error(s))");
            foreach (string s in Errors) sb.AppendLine("  ERROR   " + s);
            foreach (string s in Warnings) sb.AppendLine("  WARNING " + s);
            foreach (string s in Info) sb.AppendLine("  " + s);
            return sb.ToString();
        }
    }
}
