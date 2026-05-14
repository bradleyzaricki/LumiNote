using System;
using System.IO.Ports;
using Avalonia.Media;

namespace LumikitApp;

public class SerialHandler
{
    int _activeLedCount;
    byte[] _frameBuffer;
    byte _frameSeq;
    readonly SerialPort _serialPort;

    /// <summary>
    /// creates a new serial handler instance to handle a fixed number of lights
    /// </summary>
    /// <param name="activeLedCount"></param>
    /// <param name="port"></param>
    public SerialHandler(int activeLedCount, SerialPort port)
    {
        _activeLedCount = activeLedCount;
        _serialPort = port;
        _frameBuffer = new byte[2 + 1 + 2 + (_activeLedCount * 3) + 2];
        try
        {
            NewSerialSetup();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public void ClosePort()
    {
        if (_serialPort != null)
        {
            if (_serialPort.IsOpen)
            {
                try
                {
                    _serialPort.Close();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to close serial port.\n{e}");
                    throw;
                }

            }
        }
        return;
    }
    
    /// <summary>
    /// Create new serial 
    /// </summary>
    void NewSerialSetup()
    {
        _serialPort.Handshake = Handshake.None;
        _serialPort.WriteTimeout = -1;
        _serialPort.ReadTimeout = -1;
        _serialPort.WriteBufferSize = 65536;
        _serialPort.Open();
        SetActiveLedCount(_activeLedCount);
    }

    void EnsureBufferForCount(int count)
    {
        int needed = 2 + 1 + 2 + (count * 3) + 2;
        if (_frameBuffer.Length < needed) _frameBuffer = new byte[needed];
    }

    /// <summary>
    /// Set the active light count of the 
    /// </summary>
    /// <param name="newCount"></param>
    public void SetActiveLedCount(int newCount)
    {
        if (!_serialPort.IsOpen) return;
        if (newCount < 0) newCount = 0;
        if (newCount > 2000) newCount = 2000;

        int i = 0;
        _frameBuffer[i++] = 0xAA;
        _frameBuffer[i++] = 0x55;
        _frameBuffer[i++] = _frameSeq++;
        _frameBuffer[i++] = 0x02;
        _frameBuffer[i++] = 0x00;
        _frameBuffer[i++] = (byte)(newCount & 0xFF);
        _frameBuffer[i++] = (byte)((newCount >> 8) & 0xFF);

        ushort crc = Crc16(_frameBuffer, i);
        _frameBuffer[i++] = (byte)(crc & 0xFF);
        _frameBuffer[i++] = (byte)(crc >> 8);

        _serialPort.Write(_frameBuffer, 0, i);

        _activeLedCount = newCount;
        EnsureBufferForCount(_activeLedCount);
    }

    /// <summary>
    /// Send the frame to the hardware via serial connection 
    /// </summary>
    /// <param name="stripColors"></param>
    public void SendFrame(Color[] stripColors)
    {
        if (!_serialPort.IsOpen) return;

        int payloadLen = _activeLedCount * 3;
        EnsureBufferForCount(_activeLedCount);

        int i = 0;
        _frameBuffer[i++] = 0xAA;
        _frameBuffer[i++] = 0x55;
        _frameBuffer[i++] = _frameSeq++;
        _frameBuffer[i++] = (byte)(payloadLen & 0xFF);
        _frameBuffer[i++] = (byte)((payloadLen >> 8) & 0xFF);

        if (stripColors == null || stripColors.Length == 0)
        {
            for (int l = 0; l < _activeLedCount; l++)
            {
                _frameBuffer[i++] = 0;
                _frameBuffer[i++] = 0;
                _frameBuffer[i++] = 0;
            }
        }
        else
        {
            int n = stripColors.Length;

            if (_activeLedCount <= 1)
            {
                var c = stripColors[0];
                int a = c.A;
                _frameBuffer[i++] = (byte)((c.R * a) / 255);
                _frameBuffer[i++] = (byte)((c.G * a) / 255);
                _frameBuffer[i++] = (byte)((c.B * a) / 255);
            }
            else
            {
                for (int l = 0; l < _activeLedCount; l++)
                {
                    int src = (int)Math.Round(l * (n - 1.0) / (_activeLedCount - 1.0));
                    var c = stripColors[src];
                    int a = c.A;
                    _frameBuffer[i++] = (byte)((c.R * a) / 255);
                    _frameBuffer[i++] = (byte)((c.G * a) / 255);
                    _frameBuffer[i++] = (byte)((c.B * a) / 255);
                }
            }
        }

        ushort crc = Crc16(_frameBuffer, i);
        _frameBuffer[i++] = (byte)(crc & 0xFF);
        _frameBuffer[i++] = (byte)(crc >> 8);

        _serialPort.Write(_frameBuffer, 0, i);
    }

    static ushort Crc16(byte[] data, int len)
    {
        ushort crc = 0xFFFF;
        for (int i = 0; i < len; i++)
        {
            crc ^= data[i];
            for (int b = 0; b < 8; b++)
                crc = (ushort)((crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : (crc >> 1));
        }
        return crc;
    }
}
