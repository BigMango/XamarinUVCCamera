using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using Mango.Net.Pipelines;
using Poya.App.FaceValidation;
using Poya.App.FaceValidation.Common;
using Poya.App.FaceValidation.Common.CommandPackage;
using Poya.App.FaceValidation.Common.TypeDefine;
using Poya.App.FaceValidation.Package;

namespace Poya.Lib.FaceValidationMachineService
{
    public class FaceValidationMachineService
    {
        public void Init()
        {
            FaceValidationProcess.Instance.Init();
            FaceValidationProcess.Instance.OnDetectFace += OnDetectFace;
            FaceValidationProcess.Instance.OnDetectLiveFace += OnDetectLiveFace;


            FaceValidationProcess.Instance.OnVideoImage += OnVideoImage;
            //_centerServices.Init();
            _localService.OnClientConnected += LocalServiceOnOnClientConnected;
            _localService.OnClientDisconnect += LocalServiceOnOnClientDisconnect;
            _localService.OnChangeIP += OnLocalServiceChangeIp;
            _localService.OnReboot += OnReboot;
            _localService.OnShutdown += OnShutdown;
        }

        private bool OnShutdown()
        {
            return (bool)this.OnRequestReboot.Invoke();
        }

        private bool OnReboot()
        {
            return (bool) this.OnRequestShutdown.Invoke();
        }

        private bool OnLocalServiceChangeIp(SetIpAddressPackage arg)
        {
            return (bool) this.OnChangeIP?.Invoke(arg);
        }


        private void LocalServiceOnOnClientDisconnect(CustomSocket customsocket)
        {
            FaceValidationProcess.Instance.RemoveSocket(customsocket);
            LogUtils.Instance.ShowDebugLog("Client disconnect" , customsocket.ToString() );

            //ToConfirm //Todo0 需要安全释放Socket
            customsocket.SafeClose();
            //SafeClose
        }

        private void LocalServiceOnOnClientConnected(CustomSocket customsocket)
        {
            FaceValidationProcess.Instance.AddSocket(customsocket);
            if (!FaceValidationProcess.Instance.IsDeviceOk)
            {
                ClientCommandPackage commandPackage = new ClientCommandPackage(LocalCommand.ShowMessage);
                commandPackage.MessageDefine = ShowTextMessageDefine.DeviceError;
                customsocket.Send(commandPackage.ToByte());
            }
            LogUtils.Instance.ShowDebugLog("Client Connect" , customsocket.ToString() + ",Connect count:" + FaceValidationProcess.Instance.CustomSocketList.Count);
        }

        private CancellationTokenSource _cancellationToken = null;

        /// <summary>
        /// 启动服务
        /// </summary>
        public async void Start()
        {
            //await _centerServices.Start(_cancellationToken.Token);
            _cancellationToken = new CancellationTokenSource();
            await _localService.Start(_cancellationToken);
            FaceValidationProcess.Instance.Start(_cancellationToken);
        }

        public void Stop()
        {
            _cancellationToken.Cancel();
        }

        public bool IsStart {
            get { return ((_cancellationToken != null) && (!_cancellationToken.IsCancellationRequested)); }
        }

        //private CenterServer _centerServices = new CenterServer();
        private LocalServer _localService = new LocalServer();
        
        public Func<SetIpAddressPackage, bool> OnChangeIP;

        public Func<bool> OnRequestReboot;

        public Func<bool> OnRequestShutdown;




        private void OnVideoImage(FaceImageInfo faceImageInfo)
        {
            //Console.WriteLine("Receive bitmap data");
            LocalVideoPackage videoPackage = new LocalVideoPackage();

            //videoPackage.AddImageData(obj);
            //_localService.SendData(videoPackage);
            videoPackage.IsLive = faceImageInfo.IsLive;
            if (faceImageInfo.FaceAngle != null)
            {
                videoPackage.FaceAngle = (int)faceImageInfo.FaceAngle;
            }

            if (faceImageInfo.FaceRectList.Count == 0)
            {
                videoPackage.FaceRectangle = Rectangle.Empty;
            }
            else
            {
                if (faceImageInfo.FaceRectList[0] != null)
                {
                    videoPackage.FaceRectangle = (Rectangle) faceImageInfo.FaceRectList[0];
                }
            }

            if (faceImageInfo.ColorImage != null)
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    faceImageInfo.ColorImage.Save(stream, ImageFormat.Jpeg);
                    videoPackage.ImageLength = (int) stream.Length;
                    videoPackage.ImageData = stream.ToArray();
                }
            }

            if (faceImageInfo.DepthImage != null)
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    faceImageInfo.DepthImage.Save(stream, ImageFormat.Jpeg);
                    videoPackage.DepthImageLength = (int) stream.Length;
                    videoPackage.DepthImageData = stream.ToArray();
                }
            }

            //确保不线程重入
            CustomSocket [] connectedSocketList = FaceValidationProcess.Instance.GetConnectedSocket();

            foreach (CustomSocket customSocket in connectedSocketList)
            {
                if (customSocket != null)
                {
                    try
                    {
                        if (customSocket.IsLogin)
                        {
                            if (customSocket.ClientName != "")
                            {
                                try
                                {
                                    customSocket.Send(videoPackage.ToByte());
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine(e);
                                }
#if DEBUG
                                //??Console.WriteLine("Send to UWP bitmap data to" + customSocket.ClientName);
#endif
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        if (!customSocket.Socket.Connected)
                        {
                            //todo0 这里需要释放 Socket
                            //customsocket = null;
                        }

                        Console.WriteLine(e);
                    }
                }
            }

            //pbVideo.Image = obj;
        }

        private void OnDetectLiveFace(Bitmap obj, Rectangle rect)
        {
        }

        private void OnDetectFace(Bitmap obj, Rectangle rect)
        {
        }
    }
}