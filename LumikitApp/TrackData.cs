using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Color = Avalonia.Media.Color;

namespace LumikitApp
{
    
    public class TrackData
    {
        public Guid trackGUID { get; set; } = Guid.NewGuid();

        public Image? albumCover {get; set;}
        public string? filePath {get; set;}
        public string author { get; set; }
        public double _BPM { get; set; }
        [NotNull]
        public string _trackName { get; set; }
        public List<LightBlockData> _lightBlocks { get; set; } = new();
        public TrackData() { }


    }
    public class LightBlockData
    {
        public double X { get; set; }//position on timeline
        public double Width { get; set; }//Width of block on timeline
        public string Color { get; set; }//Saved color
        
        public string SecondColor { get; set; } 
        public List<LightBlock.Effect> BlockEffects { get; set; }
        
        public int StartLight { get; set; }
        
        public int EndLight { get; set; }
        
        public int SecondaryDualInput1 { get; set; }
        
        public int SecondaryDualInput2 { get; set; }
        
        public int SecondarySingleInput1 { get; set; }
        
        public int SecondarySingleInput2 { get; set; }
        
        public int LightIntensity { get; set; }
    }
}
