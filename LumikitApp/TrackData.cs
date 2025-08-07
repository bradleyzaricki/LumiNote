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
        public List<LightBlockData> _lightBlocks { get; set; } = new();

        public TrackData() { }


    }
    public class LightBlockData
    {
        public double X { get; set; }//position on timeline
        public double Width { get; set; }//Width of block on timeline
        public string Color { get; set; }//Saved color
    }
}
