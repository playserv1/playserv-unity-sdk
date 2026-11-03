using NUnit.Framework;

namespace Playserv.DebugTerminal.Tests
{
    public sealed class DebugTerminalRecordQueryOptionsTests
    {
        [Test]
        public void TryParse_AcceptsCompleteQueryAndBuildsTypedQuery()
        {
            var parts = DebugTerminalCommandParser.Tokenize(
                "record query --nickname Alice --min-level 3 --search ranked " +
                "--sort level --desc --limit 20 --cursor next-1 --fields nickname,level");

            Assert.IsTrue(DebugTerminalRecordQueryOptions.TryParse(parts, 2, out var options, out var error), error);
            Assert.AreEqual("Alice", options.Nickname);
            Assert.AreEqual(3, options.MinimumLevel);
            Assert.AreEqual("ranked", options.Search);
            Assert.AreEqual("level", options.Sort);
            Assert.IsTrue(options.Descending);
            Assert.AreEqual(20, options.Limit);
            Assert.AreEqual("next-1", options.Cursor);
            CollectionAssert.AreEqual(new[] { "nickname", "level" }, options.Fields);
            Assert.DoesNotThrow(() => options.Build());
        }

        [TestCase("record query --limit 0", "--limit must be between 1 and 200.")]
        [TestCase("record query --sort created", "--sort must be nickname or level.")]
        [TestCase("record query --fields nickname,email", "--fields accepts nickname, level, or nickname,level.")]
        [TestCase("record query --unknown value", "Unknown record query option '--unknown'.")]
        public void TryParse_ReportsPreciseUsageErrors(string command, string expectedError)
        {
            var parts = DebugTerminalCommandParser.Tokenize(command);

            Assert.IsFalse(DebugTerminalRecordQueryOptions.TryParse(parts, 2, out _, out var error));
            Assert.AreEqual(expectedError, error);
        }

        [Test]
        public void TryParse_BuildsRealtimeOrGroupsAndNestedIncludes()
        {
            var parts = DebugTerminalCommandParser.Tokenize(
                "record subscribe --min-level 2 " +
                "--or '{\"nickname\":\"EU\",\"minLevel\":5}' " +
                "--or '{\"nickname\":\"US\"}' " +
                "--include Guild.Owner");

            Assert.IsTrue(DebugTerminalRecordQueryOptions.TryParse(parts, 2, out var options, out var error), error);
            Assert.AreEqual(2, options.OrGroups.Count);
            Assert.AreEqual("EU", options.OrGroups[0].Nickname);
            Assert.AreEqual(5, options.OrGroups[0].MinimumLevel);
            CollectionAssert.AreEqual(new[] { "Guild.Owner" }, options.Includes);
            Assert.DoesNotThrow(() => options.Build());
        }

        [TestCase("record subscribe --or '{\"unknown\":1}'", "Unknown realtime --or field 'unknown'. Use nickname or minLevel.")]
        [TestCase("record subscribe --or '{}'", "Realtime --or group must contain nickname or minLevel.")]
        [TestCase("record subscribe --include", "--include requires a value.")]
        public void TryParse_RejectsInvalidRealtimeOptions(string command, string expectedError)
        {
            var parts = DebugTerminalCommandParser.Tokenize(command);

            Assert.IsFalse(DebugTerminalRecordQueryOptions.TryParse(parts, 2, out _, out var error));
            Assert.AreEqual(expectedError, error);
        }
    }
}
