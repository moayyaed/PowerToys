// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Subsystem.Feedback;
using System.Management.Automation.Subsystem.Prediction;

namespace WinGetCommandNotFound
{
    public sealed class WinGetCommandNotFoundFeedbackPredictor : IFeedbackProvider, ICommandPredictor
    {
        private readonly Guid _guid;

        private const int _maxSuggestions = 5;

        private List<string>? _candidates;

        public static WinGetCommandNotFoundFeedbackPredictor Singleton { get; } = new WinGetCommandNotFoundFeedbackPredictor(Init.Id);

        private WinGetCommandNotFoundFeedbackPredictor(string guid)
        {
            _guid = new Guid(guid);
        }

        public Guid Id => _guid;

        public string Name => "Windows Package Manager - WinGet";

        public string Description => "Finds missing commands that can be installed via WinGet.";

        public Dictionary<string, string>? FunctionsToDefine => null;

        /// <summary>
        /// Gets feedback based on the given commandline and error record.
        /// </summary>
        public FeedbackItem? GetFeedback(FeedbackContext context, CancellationToken token)
        {
            var target = (string)context.LastError!.TargetObject;
            if (target is not null)
            {
                bool tooManySuggestions = false;
                string packageMatchFilterField = "command";
                var pkgList = FindPackages(target, ref tooManySuggestions, ref packageMatchFilterField);
                if (pkgList.Count == 0)
                {
                    return null;
                }

                // Build list of suggestions
                _candidates = new List<string>();
                foreach (var pkg in pkgList)
                {
                    _candidates.Add(string.Format("winget install --id {0}", pkg.Members["Id"].Value.ToString()));
                }

                // Build footer message
                var footerMessage = tooManySuggestions ?
                    string.Format("Additional results can be found using \"winget search --{0} {1}\"", packageMatchFilterField, target) :
                    null;

                return new FeedbackItem(
                    "Try installing this package using winget:",
                    _candidates,
                    footerMessage,
                    FeedbackDisplayLayout.Portrait);
            }

            return null;
        }

        private System.Collections.ObjectModel.Collection<PSObject> FindPackages(string query, ref bool tooManySuggestions, ref string packageMatchFilterField)
        {
            var iss = System.Management.Automation.Runspaces.InitialSessionState.CreateDefault2();
            iss.ImportPSModule(new[] { "Microsoft.WinGet.Client" });
            var ps = PowerShell.Create(iss);

            var common = new Hashtable()
            {
                ["Count"] = _maxSuggestions,
                ["Source"] = "winget",
            };

            // 1) Search by command
            var pkgList = ps.AddCommand("Find-WinGetPackage")
                .AddParameter("Command", query)
                .AddParameter("MatchOption", "StartsWithCaseInsensitive")
                .AddParameters(common)
                .Invoke();
            if (pkgList.Count > 0)
            {
                tooManySuggestions = pkgList.Count > _maxSuggestions;
                packageMatchFilterField = "command";
                return pkgList;
            }

            // 2) No matches found,
            //    search by name
            ps.Commands.Clear();
            pkgList = ps.AddCommand("Find-WinGetPackage")
                .AddParameter("Name", query)
                .AddParameter("MatchOption", "ContainsCaseInsensitive")
                .AddParameters(common)
                .Invoke();
            if (pkgList.Count > 0)
            {
                tooManySuggestions = pkgList.Count > _maxSuggestions;
                packageMatchFilterField = "name";
                return pkgList;
            }

            // 3) No matches found,
            //    search by moniker
            ps.Commands.Clear();
            pkgList = ps.AddCommand("Find-WinGetPackage")
                .AddParameter("Moniker", query)
                .AddParameter("MatchOption", "ContainsCaseInsensitive")
                .AddParameters(common)
                .Invoke();
            tooManySuggestions = pkgList.Count > _maxSuggestions;
            packageMatchFilterField = "moniker";
            return pkgList;
        }

        public bool CanAcceptFeedback(PredictionClient client, PredictorFeedbackKind feedback)
        {
            return feedback switch
            {
                PredictorFeedbackKind.CommandLineAccepted => true,
                _ => false,
            };
        }

        public SuggestionPackage GetSuggestion(PredictionClient client, PredictionContext context, CancellationToken cancellationToken)
        {
            if (_candidates is not null)
            {
                string input = context.InputAst.Extent.Text;
                List<PredictiveSuggestion>? result = null;

                foreach (string c in _candidates)
                {
                    if (c.StartsWith(input, StringComparison.OrdinalIgnoreCase))
                    {
                        result ??= new List<PredictiveSuggestion>(_candidates.Count);
                        result.Add(new PredictiveSuggestion(c));
                    }
                }

                if (result is not null)
                {
                    return new SuggestionPackage(result);
                }
            }

            return default;
        }

        public void OnCommandLineAccepted(PredictionClient client, IReadOnlyList<string> history)
        {
            // Reset the candidate state.
            _candidates = null;
        }
    }
}
