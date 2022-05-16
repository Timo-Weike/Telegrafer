using DynamicData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Telegrafer.Utils
{
    public class OldInputs
    {
        private const string OldPayloadsFileName = "old-payloads.dat";

        private static readonly string OldPayloadsFilePath = Path.Combine(App.AppDataDirPath, OldPayloadsFileName);

        public static ObservableCollection<string> OldPayloads { get; }

        public static void AddNewPayload(string payload)
        {
            if (string.IsNullOrEmpty(payload))
            {
                return;
            }

            if (OldPayloads.Contains(payload))
            {
                OldPayloads.Move(OldPayloads.IndexOf(payload), 0);
            }
            else
            {
                OldPayloads.Insert(0, payload);
            }

            if (OldPayloads.Count > 20)
            {
                var olds = OldPayloads.Take(20).ToArray();
                OldPayloads.Clear();
                OldPayloads.AddRange(olds);
            }
        }

        static OldInputs()
        {
            if (File.Exists(OldPayloadsFilePath))
            {
                var lines = File.ReadAllLines(OldPayloadsFilePath);

                OldPayloads = new ObservableCollection<string>(lines);
            }
            else
            {
                OldPayloads = new ObservableCollection<string>();
            }
        }

        public static void Save()
        {
            File.WriteAllLines(OldPayloadsFileName, OldPayloads);
        }
    }
}
