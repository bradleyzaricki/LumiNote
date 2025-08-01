using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using SpotifyAPI.Web;
namespace SpotifyInformationConsole
{
    class Program
    {
        static void Main(string[] args)
        {
            SpotifyProvider provider = new SpotifyProvider();
            provider.InitializeClient();



            Console.Read();
        }
    }
}