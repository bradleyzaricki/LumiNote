using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LumikitApp
{
    internal class TrackData
    {
        public double _BPM { get; set; }
        [NotNull]
        public string _trackID { get; set; }
        public string _trackName { get; set; }

        public TrackData() { }


    }
}
