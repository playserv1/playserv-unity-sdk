using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;

namespace Playserv.DebugTerminal.Tests
{
    public sealed class DebugTerminalCommandParserTests
    {
        [Test]
        public void Tokenize_NormalizesInvisibleCharactersAndPreservesQuotedValues()
        {
            var tokens = DebugTerminalCommandParser.Tokenize(
                "\u200Brecord\tquery --nickname \"Player One\"\r\n--limit 25");

            CollectionAssert.AreEqual(
                new[] { "record", "query", "--nickname", "Player One", "--limit", "25" },
                tokens);
        }

        [Test]
        public void Tokenize_PreservesJsonWrappedInSingleQuotes()
        {
            var tokens = DebugTerminalCommandParser.Tokenize(
                "match find ranked --params '{\"mode\":\"duo queue\",\"skill\":42}'");

            Assert.AreEqual("{\"mode\":\"duo queue\",\"skill\":42}", tokens[4]);
        }

        [Test]
        public void Arguments_ParsesRepeatedOptionsAndRejectsNonObjectMatchmakingJson()
        {
            var tokens = DebugTerminalCommandParser.Tokenize(
                "code call echo --query one=1 --query two=2 --timeout 15");

            Assert.IsTrue(DebugTerminalArguments.TryParse(
                tokens,
                3,
                new[] { "query", "timeout" },
                new string[0],
                out var arguments,
                out var error), error);
            CollectionAssert.AreEqual(new[] { "one=1", "two=2" }, arguments.GetAll("query"));
            Assert.IsTrue(arguments.TryGetInt("timeout", 0, 1, 3600, out var timeout, out error), error);
            Assert.AreEqual(15, timeout);

            Assert.IsFalse(DebugTerminalArguments.TryParseJson("[1,2]", true, out _, out error));
            StringAssert.Contains("object", error);
            Assert.IsTrue(DebugTerminalArguments.TryParseJson("{\"mode\":\"duo\"}", true, out var value, out error), error);
            Assert.IsInstanceOf<IDictionary<string, object>>(value);
        }

        [Test]
        public void CodeArguments_RejectCustomHeadersBeforeInvocation()
        {
            var tokens = DebugTerminalCommandParser.Tokenize(
                "code invoke POST echo --header Authorization=secret");

            Assert.IsFalse(DebugTerminalArguments.TryParse(
                tokens,
                4,
                new[] { "body", "query", "version", "timeout" },
                new string[0],
                out _,
                out var error));
            StringAssert.Contains("Unknown option '--header'", error);
        }

        [Test]
        public void Catalog_ProvidesGroupedSuggestionsUsageAndAutocomplete()
        {
            var suggestions = DebugTerminalCommandCatalog.GetSuggestions(
                "record sub",
                DebugTerminalCommandParser.Tokenize);

            CollectionAssert.AreEqual(new[] { "record subscribe" }, suggestions);
            Assert.AreEqual(
                "record subscribe ",
                DebugTerminalCommandCatalog.TryAutocomplete(
                    "record sub",
                    DebugTerminalCommandParser.Tokenize,
                    values => values.First()));
            Assert.AreEqual(
                "record subscribe [query options] [--include relation.path] [--or json]",
                DebugTerminalCommandCatalog.GetUsage(
                    DebugTerminalCommandParser.Tokenize("record subscribe"),
                    suggestions));

            CollectionAssert.Contains(DebugTerminalCommandCatalog.TopLevelCommands, "analytics");
            CollectionAssert.Contains(DebugTerminalCommandCatalog.TopLevelCommands, "code");
            CollectionAssert.Contains(DebugTerminalCommandCatalog.TopLevelCommands, "catalog");
            CollectionAssert.Contains(DebugTerminalCommandCatalog.TopLevelCommands, "storefront");
            CollectionAssert.Contains(DebugTerminalCommandCatalog.TopLevelCommands, "match");
            CollectionAssert.Contains(DebugTerminalCommandCatalog.TopLevelCommands, "platform");
            CollectionAssert.Contains(DebugTerminalCommandCatalog.TopLevelCommands, "table");
            CollectionAssert.Contains(DebugTerminalCommandCatalog.TopLevelCommands, "server");

            CollectionAssert.Contains(
                DebugTerminalCommandCatalog.GetSuggestions("subscription ref", DebugTerminalCommandParser.Tokenize),
                "subscription refresh");
            CollectionAssert.Contains(
                DebugTerminalCommandCatalog.GetSuggestions("code down", DebugTerminalCommandParser.Tokenize),
                "code download");
            CollectionAssert.Contains(
                DebugTerminalCommandCatalog.GetSuggestions("server real", DebugTerminalCommandParser.Tokenize),
                "server realtime");
        }

        [TestCase("bind", "bind <playerId> [polling|transport]")]
        [TestCase("refresh", "refresh")]
        [TestCase("spawn", "spawn [assetName]")]
        public void Catalog_KeepsLegacyCommandUsage(string command, string expected)
        {
            var tokens = DebugTerminalCommandParser.Tokenize(command);
            Assert.AreEqual(expected, DebugTerminalCommandCatalog.GetUsage(tokens, null));
        }
    }
}
