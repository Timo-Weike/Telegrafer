namespace Telegrafer.Communication
{
    public enum ReceivedChunkKind
    {
        /// <summary>A complete telegram from STX to ETX.</summary>
        Frame,

        /// <summary>A telegram that started with STX but did not end with ETX.</summary>
        IncompleteFrame,

        /// <summary>Bytes outside of an STX/ETX frame, e.g. a text line or a single ACK byte.</summary>
        Data,
    }

    public sealed class ReceivedChunk
    {
        public ReceivedChunk(byte[] data, ReceivedChunkKind kind)
        {
            this.Data = data;
            this.Kind = kind;
        }

        public byte[] Data { get; }

        public ReceivedChunkKind Kind { get; }
    }

    /// <summary>
    /// Splits a received TCP byte stream into telegrams.
    /// </summary>
    /// <remarks>
    /// TCP does not preserve message boundaries: one telegram can arrive in several reads and one read can contain
    /// several telegrams. The framer collects the bytes and emits:
    /// <list type="bullet">
    /// <item>a frame from STX to ETX as soon as the ETX has arrived; CR and LF that directly follow the ETX in the
    /// same read are part of the frame,</item>
    /// <item>bytes outside of a frame at every LF and before the next STX,</item>
    /// <item>everything that is still pending when <see cref="Flush"/> is called, which the connection does after a
    /// short time without new data.</item>
    /// </list>
    /// The framer is not thread-safe.
    /// </remarks>
    public sealed class TelegramFramer
    {
        public const int DefaultMaxChunkLength = 64 * 1024;

        private readonly List<byte> pending = new();
        private readonly int maxChunkLength;
        private bool insideFrame;

        public TelegramFramer(int maxChunkLength = DefaultMaxChunkLength)
        {
            if (maxChunkLength < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(maxChunkLength));
            }

            this.maxChunkLength = maxChunkLength;
        }

        /// <summary>
        /// Gets a value indicating whether bytes are waiting for the end of their telegram.
        /// </summary>
        public bool HasPendingData => this.pending.Count > 0;

        /// <summary>
        /// Gets a value indicating whether the pending bytes belong to a frame that started with STX.
        /// </summary>
        public bool IsInsideFrame => this.insideFrame;

        /// <summary>
        /// Processes received bytes and returns all chunks that are complete.
        /// </summary>
        public IReadOnlyList<ReceivedChunk> Append(ReadOnlySpan<byte> data)
        {
            List<ReceivedChunk>? chunks = null;

            for (int i = 0; i < data.Length; i++)
            {
                var b = data[i];

                if (this.insideFrame)
                {
                    if (b == TelegramCodec.Stx)
                    {
                        // A new frame starts before the previous one was terminated.
                        this.Emit(ReceivedChunkKind.IncompleteFrame, ref chunks);
                        this.pending.Add(b);
                        this.insideFrame = true;
                    }
                    else
                    {
                        this.pending.Add(b);

                        if (b == TelegramCodec.Etx)
                        {
                            if (i + 1 < data.Length && data[i + 1] == TelegramCodec.Cr)
                            {
                                this.pending.Add(data[++i]);
                            }

                            if (i + 1 < data.Length && data[i + 1] == TelegramCodec.Lf)
                            {
                                this.pending.Add(data[++i]);
                            }

                            this.Emit(ReceivedChunkKind.Frame, ref chunks);
                        }
                    }
                }
                else if (b == TelegramCodec.Stx)
                {
                    if (this.pending.Count > 0)
                    {
                        this.Emit(ReceivedChunkKind.Data, ref chunks);
                    }

                    this.pending.Add(b);
                    this.insideFrame = true;
                }
                else
                {
                    this.pending.Add(b);

                    if (b == TelegramCodec.Lf)
                    {
                        this.Emit(ReceivedChunkKind.Data, ref chunks);
                    }
                }

                if (this.pending.Count >= this.maxChunkLength)
                {
                    // Protects against a peer that never sends a terminator. The rest of a frame is emitted when its
                    // ETX arrives.
                    var wasInsideFrame = this.insideFrame;
                    this.Emit(wasInsideFrame ? ReceivedChunkKind.IncompleteFrame : ReceivedChunkKind.Data, ref chunks);
                    this.insideFrame = wasInsideFrame;
                }
            }

            return (IReadOnlyList<ReceivedChunk>?)chunks ?? Array.Empty<ReceivedChunk>();
        }

        /// <summary>
        /// Emits the pending bytes regardless of whether their telegram is complete.
        /// </summary>
        public ReceivedChunk? Flush()
        {
            if (this.pending.Count == 0)
            {
                return null;
            }

            var chunk = new ReceivedChunk(
                this.pending.ToArray(),
                this.insideFrame ? ReceivedChunkKind.IncompleteFrame : ReceivedChunkKind.Data);

            this.pending.Clear();
            this.insideFrame = false;

            return chunk;
        }

        private void Emit(ReceivedChunkKind kind, ref List<ReceivedChunk>? chunks)
        {
            chunks ??= new List<ReceivedChunk>();
            chunks.Add(new ReceivedChunk(this.pending.ToArray(), kind));

            this.pending.Clear();
            this.insideFrame = false;
        }
    }
}
