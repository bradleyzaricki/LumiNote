using System;
using System.IO;
using System.IO.Ports;
using Avalonia.Media;

namespace LumikitApp;

public class SerialHandler
{
    int _ledCount;
    private readonly byte[] _frameBuffer;
    byte _frameSeq = 0;
    SerialPort _serialPort;

    public SerialHandler(int numLedToUpdate, SerialPort port)
    {
        _ledCount = numLedToUpdate;
        _frameBuffer = new byte[2 + 1 + 2 + _ledCount * 3 + 2];
        _serialPort = port;
        
        newSerialSetup();
    }

    void newSerialSetup()
    {
        _serialPort.Handshake = Handshake.None;
        _serialPort.WriteTimeout = -1;
        _serialPort.ReadTimeout = -1;
        _serialPort.WriteBufferSize = 16384;
        try
        {
            _serialPort.Open();
        }
        catch (Exception)
        {
            Console.WriteLine("Failed to open serial port, live serial output feature not available," +
                              " please refer to Luminote's built in lighting for visual feedback");
        }    
    }


    public void SendFrame(Color[] stripColors)
    {
        if(!_serialPort.IsOpen) return;
        
        
        int payloadLen = _ledCount * 3;

        int i = 0;
        _frameBuffer[i++] = 0xAA;
        _frameBuffer[i++] = 0x55;
        _frameBuffer[i++] = _frameSeq++;
        _frameBuffer[i++] = (byte)(payloadLen & 0xFF);
        _frameBuffer[i++] = (byte)(payloadLen >> 8);

        if (stripColors == null || stripColors.Length == 0)
        {
             
            for (int l = 0; l < _ledCount; l++)
            {
                _frameBuffer[i++] = 0;
                _frameBuffer[i++] = 0;
                _frameBuffer[i++] = 0;
            }
        }
        else
        {
            int n = stripColors.Length; // should be 1000

            for (int l = 0; l < _ledCount; l++)
            {
                int src = (int)Math.Round(l * (n - 1.0) / (_ledCount - 1.0));
                var c = stripColors[src];

                int a = c.A;
                _frameBuffer[i++] = (byte)((c.R * a) / 255);
                _frameBuffer[i++] = (byte)((c.G * a) / 255);
                _frameBuffer[i++] = (byte)((c.B * a) / 255);
            }

        }

        ushort crc = Crc16(_frameBuffer, i);
        _frameBuffer[i++] = (byte)(crc & 0xFF);
        _frameBuffer[i++] = (byte)(crc >> 8);

        _serialPort.BaseStream.Write(_frameBuffer, 0, i);
    }
        
    ushort Crc16(byte[] data, int len)
    {
        ushort crc = 0xFFFF;
        for (int i = 0; i < len; i++)
        {
            crc ^= data[i];
            for (int b = 0; b < 8; b++)
                crc = (ushort)((crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1);
        }
        return crc;
    }
}