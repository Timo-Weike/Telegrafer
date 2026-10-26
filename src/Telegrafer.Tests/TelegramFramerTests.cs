using Telegrafer.Communication;
using Xunit;
using static Telegrafer.Tests.TelegramCodecTests;

namespace Telegrafer.Tests
{
    public class TelegramFramerTests
    {
        [Fact]
        public void Append_EmitsACompleteFrame()
        {
            var framer = new TelegramFramer();

            var chunks = framer.Append(FromHex("02 41 42 03"));

            AssertChunks(chunks, (ReceivedChunkKind.Frame, "02 41 42 03"));
            Assert.False(framer.HasPendingData);
        }

        [Fact]
        public void Append_JoinsAFrameThatArrivesInSeveralReads()
        {
            var framer = new TelegramFramer();

            Assert.Empty(framer.Append(FromHex("02 41")));
            Assert.True(framer.HasPendingData);
            Assert.True(framer.IsInsideFrame);
            Assert.Empty(framer.Append(FromHex("42")));

            AssertChunks(framer.Append(FromHex("43 03")), (ReceivedChunkKind.Frame, "02 41 42 43 03"));
        }

        [Fact]
        public void Append_SplitsSeveralFramesOfOneRead()
        {
            var framer = new TelegramFramer();

            var chunks = framer.Append(FromHex("02 41 03 02 42 03 02 43"));

            AssertChunks(chunks, (ReceivedChunkKind.Frame, "02 41 03"), (ReceivedChunkKind.Frame, "02 42 03"));
            Assert.True(framer.IsInsideFrame);
        }

        [Fact]
        public void Append_KeepsALineEndingThatDirectlyFollowsTheEtx()
        {
            var framer = new TelegramFramer();

            var chunks = framer.Append(FromHex("02 41 03 0D 0A 02 42 03 0A"));

            AssertChunks(chunks, (ReceivedChunkKind.Frame, "02 41 03 0D 0A"), (ReceivedChunkKind.Frame, "02 42 03 0A"));
        }

        [Fact]
        public void Append_EmitsBytesOutsideOfAFrameBeforeTheNextStx()
        {
            var framer = new TelegramFramer();

            var chunks = framer.Append(FromHex("06 02 41 03"));

            AssertChunks(chunks, (ReceivedChunkKind.Data, "06"), (ReceivedChunkKind.Frame, "02 41 03"));
        }

        [Fact]
        public void Append_SplitsTextLinesOutsideOfFrames()
        {
            var framer = new TelegramFramer();

            var chunks = framer.Append(FromHex("48 49 0D 0A 48 4F 0A 58"));

            AssertChunks(chunks, (ReceivedChunkKind.Data, "48 49 0D 0A"), (ReceivedChunkKind.Data, "48 4F 0A"));
            Assert.True(framer.HasPendingData);
            Assert.False(framer.IsInsideFrame);
        }

        [Fact]
        public void Append_DoesNotSplitAFrameAtLineFeeds()
        {
            var framer = new TelegramFramer();

            var chunks = framer.Append(FromHex("02 41 0A 42 03"));

            AssertChunks(chunks, (ReceivedChunkKind.Frame, "02 41 0A 42 03"));
        }

        [Fact]
        public void Append_MarksAFrameAsIncompleteWhenTheNextStxArrivesFirst()
        {
            var framer = new TelegramFramer();

            var chunks = framer.Append(FromHex("02 41 02 42 03"));

            AssertChunks(chunks, (ReceivedChunkKind.IncompleteFrame, "02 41"), (ReceivedChunkKind.Frame, "02 42 03"));
        }

        [Fact]
        public void Flush_EmitsPendingBytes()
        {
            var framer = new TelegramFramer();
            framer.Append(FromHex("02 41"));

            var incomplete = framer.Flush();
            framer.Append(FromHex("58 59"));
            var data = framer.Flush();

            Assert.Equal(ReceivedChunkKind.IncompleteFrame, incomplete!.Kind);
            Assert.Equal("02 41", TelegramCodec.FormatAsHex(incomplete.Data));
            Assert.Equal(ReceivedChunkKind.Data, data!.Kind);
            Assert.Equal("58 59", TelegramCodec.FormatAsHex(data.Data));
            Assert.Null(framer.Flush());
        }

        [Fact]
        public void Append_LimitsTheLengthOfAChunk()
        {
            var framer = new TelegramFramer(maxChunkLength: 4);

            var chunks = framer.Append(FromHex("02 41 42 43 44 45 03"));

            AssertChunks(chunks, (ReceivedChunkKind.IncompleteFrame, "02 41 42 43"), (ReceivedChunkKind.Frame, "44 45 03"));
        }

        private static void AssertChunks(IReadOnlyList<ReceivedChunk> chunks, params (ReceivedChunkKind Kind, string Hex)[] expected)
        {
            Assert.Equal(expected.Length, chunks.Count);

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i].Kind, chunks[i].Kind);
                Assert.Equal(expected[i].Hex, TelegramCodec.FormatAsHex(chunks[i].Data));
            }
        }
    }
}
