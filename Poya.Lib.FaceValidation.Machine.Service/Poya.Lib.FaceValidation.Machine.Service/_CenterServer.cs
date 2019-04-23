using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mango.Net.Pipelines;
using Poya.App.FaceValidation;
using Poya.App.FaceValidation.Common;

namespace Poya.Lib.FaceValidationMachineService
{
    /// <summary>
    /// 中心通讯的服务
    /// </summary>
    public class CenterServer
    {
        private static int totalReceive = 0;
        private static int totalProcess = 0;
        private static int totalAdvance = 0;
        private Socket listenSocket = null;
        public void Init()
        {
            listenSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            listenSocket.Bind(new IPEndPoint(IPAddress.Any, FaceValidationConfig.Instance.CenterServerPort));

            Console.WriteLine("Listening on port " + FaceValidationConfig.Instance.CenterServerPort);
        }

        private CancellationToken _cancellationToken;

        public async Task Start(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            listenSocket.Listen(5);
            _= await Task.Factory.StartNew(SocketListen);
        }

        private Socket socket = null;

        private async Task SocketListen()
        {
            while (true)
            {
                socket = await listenSocket.AcceptAsync();
                //弃元 
                _ = ProcessLinesAsync(socket);
            }
        }

    

        private async Task ProcessLinesAsync(Socket socket)
        {
            //Console.WriteLine($"\r\n\r\n\r\n\r\n\r\n\r\n[{socket.RemoteEndPoint}]: connected");

            var pipe = new Pipe();
            Task writing = FillPipeAsync(socket, pipe.Writer);
            Task reading = ReadPipeAsync(socket, pipe.Reader);

            await Task.WhenAll(reading, writing);

            Console.WriteLine($"[{socket.RemoteEndPoint}]: disconnected,TotalReveice:{totalReceive} TotalProcess:{totalProcess} TotalAdcance:{totalAdvance}");
        }

        private const string PostfixText = "fafbfcfd";

        static Object thisLock = new Object();
        private async Task FillPipeAsync(Socket socket, PipeWriter writer)
        {
            const int minimumBufferSize = 512;

            while (true)
            {
                try
                {
                    // Request a minimum of 512 bytes from the PipeWriter
                    Memory<byte> memory = writer.GetMemory(minimumBufferSize);

                    int bytesRead = await socket.ReceiveAsync(memory, SocketFlags.None);
                    if (bytesRead == 0)
                    {
                        break;
                    }
                    //Console.WriteLine($"===================Read Data:{bytesRead}================");
                    // Tell the PipeWriter how much was read
                    totalReceive += bytesRead;
                    writer.Advance(bytesRead);
                }
                catch
                {
                    break;
                }

                // Make the data available to the PipeReader
                FlushResult result = await writer.FlushAsync();

                if (result.IsCompleted)
                {
                    break;
                }
            }

            // Signal to the reader that we're done writing
            writer.Complete();
        }

        private static async Task ReadPipeAsync(Socket socket, PipeReader reader)
        {
            while (true)
            {
                ReadResult result = await reader.ReadAsync().ConfigureAwait(false);
                //Console.WriteLine($"-------------read buffer :{result.Buffer.Length}");
                //lock (thisLock)
                {
                    ReadOnlySequence<byte> buffer = result.Buffer;
                    SequencePosition? positionPrefix = null;
                    SequencePosition? positionPostfix = null;
                    bool needContinue = true;
                    do
                    {
                        //positionPrefix = buffer.PositionOf2(Encoding.Default.GetBytes(PrefixText));
                        //if (positionPrefix != null)
                        //{
                        //}

                        //positionPostfix = buffer.PositionOf2(Encoding.Default.GetBytes(PostfixText));
                        positionPostfix = buffer.PositionOf((byte)'\n');

                        //if (positionPrefix == null)
                        //{
                        //    positionPrefix = await GetPrefixPosition(buffer, result.Buffer.Length);
                        //    if (positionPrefix != null)
                        //    {
                        //        var next = buffer.GetPosition(1, positionPrefix.Value);
                        //        buffer = buffer.Slice(next);
                        //        //Console.WriteLine($"Find positionPrefix :{positionPrefix.Value.GetInteger()}");
                        //    }
                        //}

                        //positionPostfix = await GetPostfixPosition(buffer, result.Buffer.Length);
                        //if ((positionPrefix != null) && (positionPostfix != null))
                        if (positionPostfix != null)
                        {
                            //var line = buffer.Slice(positionPrefix.Value, positionPostfix.Value);
                            var line = buffer.Slice(0, positionPostfix.Value);
                            ProcessPackage(socket, line);
                            var next = buffer.GetPosition(1, positionPostfix.Value);

                            // Skip what we've already processed including \n
                            ////Console.WriteLine($"Move to {next.GetInteger()} ,Total : {buffer.Length}");
                            buffer = buffer.Slice(next);
                            ////Console.WriteLine($"Move after Total : {buffer.Length}");


                            //positionPrefix = null;
                            //positionPostfix = null;
                            //needContinue = true;
                        }
                        else
                        {
                            needContinue = false;
                        }
                    } while (positionPostfix != null);

                    // We sliced the buffer until no more data could be processed
                    // Tell the PipeReader how much we consumed and how much we left to process
                    reader.AdvanceTo(buffer.Start, buffer.End);
                    int advanceCount = buffer.End.GetInteger() - buffer.Start.GetInteger();
                    totalAdvance += advanceCount;
                    Console.WriteLine($"=========== AdvanceTo total: {advanceCount} start:{buffer.Start.GetInteger()} - end:{buffer.End.GetInteger()} ");

                    if (result.IsCompleted)
                    {
                        break;
                    }
                }
            }

            reader.Complete();
        }

        private static void ProcessPackage(Socket socket, in ReadOnlySequence<byte> buffer)
        {
            //Console.Write($"[{socket.RemoteEndPoint}]: ");
            foreach (var segment in buffer)
            {
                totalProcess += (int)buffer.Length;
#if NETCOREAPP2_1
                //Console.Write(Encoding.UTF8.GetString(segment.Span));
#else

                string str = Encoding.UTF8.GetString(segment);
                if (str.Length > 20)
                {
                    str = str.Substring(0, 20);
                }

                //Console.Write(str);
#endif
            }
            //Console.WriteLine();
        }
    }
}