using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mango.Common;
using Mango.Net.Pipelines;
using Poya.App.FaceValidation;
using Poya.App.FaceValidation.Common;
using Poya.App.FaceValidation.Common.CenterPackage;
using Poya.App.FaceValidation.Common.CommandPackage;
using Poya.App.FaceValidation.Common.TypeDefine;
using Poya.Lib.Device;
using Poya.Lib.Device.Camera.Astra3D;

namespace Poya.Lib.FaceValidationMachineService
{
    /// <summary>
    /// 与Xamarin客户端通讯的Service
    /// </summary>
    public class LocalServer : TcpServerBase
    {
        public  override void InitSocket()
        {
            listenSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            string ip = IPUtils.GetLocalIP();
            //if (ip == "127.0.0.1")
            //{
            //    return;
            //}

            //listenSocket.Bind(new IPEndPoint(IPAddress.Loopback, FaceValidationConfig.Instance.LocalServerPort));
            //listenSocket.Bind(new IPEndPoint(IPAddress.Parse(ip), FaceValidationConfig.Instance.LocalServerPort));
            listenSocket.Bind(new IPEndPoint(IPAddress.Any, FaceValidationConfig.Instance.LocalServerPort));

            LogUtils.Instance.ShowDebugLog($"Listening on {ip}:"  + FaceValidationConfig.Instance.LocalServerPort);
            Console.WriteLine("Listening on port " + FaceValidationConfig.Instance.LocalServerPort);
        }

        //private async Task ProcessLinesAsync(Socket socket)
        //{
        //    //Console.WriteLine($"\r\n\r\n\r\n\r\n\r\n\r\n[{socket.RemoteEndPoint}]: connected");

        //    var pipe = new Pipe();
        //    Task writing = FillPipeAsync(socket, pipe.Writer);
        //    Task reading = ReadPipeAsync(socket, pipe.Reader);

        //    await Task.WhenAll(reading, writing);

        //    Console.WriteLine($"[{socket.RemoteEndPoint}]: disconnected,TotalReveice:{totalReceive} TotalProcess:{totalProcess} TotalAdcance:{totalAdvance}");
        //}


        public LocalServer()
        {
        }

        public Func <SetIpAddressPackage, bool> OnChangeIP;


        public Func<bool> OnReboot;
        public Func<bool> OnShutdown;
        protected override void ProcessPackage(CustomSocket customSocket, PackageHeader packageHeader, byte[] bsPackageBuffer)
        {
            MGDeviceCameraAstra3D deviceCameraAstra3D = null;
            CustomSocket localCustomSocket = FaceValidationProcess.Instance.GetLocalSocket();
            switch (packageHeader.Type)
            {
                case PackageType.Command:
                    ClientCommandPackage commandPackage = new ClientCommandPackage();
                    if (commandPackage.FromByte(bsPackageBuffer))
                    {
                        switch (commandPackage.Command)
                        {
                            case LocalCommand.Undefined:
                                break;
                            case LocalCommand.EnterCollectFlow:
                                FaceValidationProcess.Instance.ChangeFlow(App.FaceValidation.Common.FaceAppFlow.CollectFlow);
                                break;
                            case LocalCommand.EnterMainFlow:
                                FaceValidationProcess.Instance.ChangeFlow(App.FaceValidation.Common.FaceAppFlow.MainFlow);
                                break;
                            case LocalCommand.EnterNoCardFlow:
                                FaceValidationProcess.Instance.ChangeFlow(App.FaceValidation.Common.FaceAppFlow.NoCardFlow);
                                break;
                            case LocalCommand.EnterEmptyFlow:
                                FaceValidationProcess.Instance.ChangeFlow(App.FaceValidation.Common.FaceAppFlow.Undefined);
                                break;
                            case LocalCommand.DoorOpen:
                                break;

                            case LocalCommand.OpenCamera:
                                FaceValidationProcess.Instance.OpenCamera();
                                break;

                            case LocalCommand.CloseCamera:
                                FaceValidationProcess.Instance.CloseCamera();
                                break;

                            case LocalCommand.FaceDetect:
                                try
                                {
                                    FaceDetectPackage faceDetectPackage = new FaceDetectPackage();
                                    faceDetectPackage.FromByte(bsPackageBuffer);

                                    if (faceDetectPackage.ImageData != null)
                                    {
                                        FacedetectResult[] facedetectResults = FaceValidationProcess.Instance.GetFaceDetect(faceDetectPackage.ImageData);
                                        FaceDetectResultPackage faceDetectResultPackage = new FaceDetectResultPackage();
                                        faceDetectResultPackage.FacedetectResults.AddRange(facedetectResults);
                                        if (localCustomSocket != null)
                                        {
                                            localCustomSocket.Send(faceDetectResultPackage.ToByte());
                                        }
                                        LogUtils.Instance.ShowDebugLog("获取人脸成功");
                                    }
                                }
                                catch (Exception e)
                                {
                                    LogUtils.Instance.ShowDebugLog("获取人脸失败:" + e.Message);
                                }
                                break;

                            case LocalCommand.ShowCenterMessage:
                                CenterTextMessage centerTextMessage = new CenterTextMessage();
                                centerTextMessage.FromByte(bsPackageBuffer);
                                if (localCustomSocket != null)
                                {
                                    localCustomSocket.Send(centerTextMessage.ToByte());
                                }

                                break;

                            case LocalCommand.ShowMessage:

                            case LocalCommand.SetConfig:
                                SetConfigPackage setConfigPackage = new SetConfigPackage();
                                if (setConfigPackage.FromByte(bsPackageBuffer))
                                {
                                    UpdateConfig(setConfigPackage);
                                }
                                break;

                            case LocalCommand.RestartDevice:
                                OnReboot?.Invoke();
                                break;

                            case LocalCommand.ShutdownDevice:
                                OnShutdown?.Invoke();
                                break;

                            case LocalCommand.SetIpAddress:
                                SetIpAddressPackage setIpAddressPackage = new SetIpAddressPackage();

                                //ClientCommandPackage clientCommandPackage = new ClientCommandPackage(LocalCommand.SetIpAddress);
                                //clientCommandPackage.MessageDefine = MessageDefine.Undefined; 

                                if (setIpAddressPackage.FromByte(bsPackageBuffer))
                                {
                                    bool result = (bool)OnChangeIP?.Invoke(setIpAddressPackage);
                                    //if (!UpdateIPAddress(setIpAddressPackage))
                                    //{
                                        //clientCommandPackage.MessageDefine = MessageDefine.NetworkError;
                                    //}
                                }
                                LogUtils.Instance.ShowDebugLog("Change ip address and Stop server");
                                //CancelToken.Cancel();
                                //customSocket.Send(clientCommandPackage.ToByte());
                                break;
                            case LocalCommand.Login:
                                LoginPackage loginPackage = new LoginPackage();
                                if (loginPackage.FromByte(bsPackageBuffer))
                                {
                                    if (loginPackage.Password == FaceValidationConfig.Instance.ClientLoginPassword)
                                    {
                                        customSocket.IsLogin = true;
                                        customSocket.ClientUid = loginPackage.ClientUid;
                                        customSocket.ClientName = loginPackage.ClientName;
                                        customSocket.Send(loginPackage.ToByte());
                                    }
                                }
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                    break;
            }
        }

        private bool UpdateConfig(SetConfigPackage setConfigPackage)
        {
            string oldCenterUrl = FaceValidationConfig.Instance.CenterServiceUrl;

            if (setConfigPackage.ConfigConent != "")
            {
                FaceValidationConfig.Instance.LoadFromXml(setConfigPackage.ConfigConent);
                FaceValidationConfig.Instance.ReloadConfig();
                FaceValidationConfig.Instance.WriteConfig();
                DebugLogger.LogDebugMessage("更新全部参数");
            }
            else
            {
                switch (setConfigPackage.ConfigName)
                {
                    case "center_service_url":
                        FaceValidationConfig.Instance.CenterServiceUrl = setConfigPackage.ConfigValue;
                        break;

                    case "match_ic_card_threshold_level":
                        FaceValidationConfig.Instance.MatchICCardThresholdLevel = ConvertUtil.ToInt(setConfigPackage.ConfigValue, FaceValidationConfig.Instance.MatchICCardThresholdLevel);
                        break;
                    case "match_id_card_threshold_level":
                        FaceValidationConfig.Instance.MatchIdCardThresholdLevel = ConvertUtil.ToInt(setConfigPackage.ConfigValue, FaceValidationConfig.Instance.MatchIdCardThresholdLevel);
                        break;
                    case "relay_open_duration":
                        FaceValidationConfig.Instance.RelayOpenDuration = ConvertUtil.ToInt(setConfigPackage.ConfigValue, FaceValidationConfig.Instance.RelayOpenDuration);
                    break;

                    case "auto_update_idcard_face":
                        FaceValidationConfig.Instance.AutoUpdateIDCardFace = ConvertUtil.ToBool(setConfigPackage.ConfigValue, FaceValidationConfig.Instance.AutoUpdateIDCardFace);
                    break;

                    case "relay_enabled":
                        FaceValidationConfig.Instance.RelayEnabled = ConvertUtil.ToBool(setConfigPackage.ConfigValue, FaceValidationConfig.Instance.RelayEnabled);
                        break;

                    case "upw_data_path":
                        FaceValidationConfig.Instance.UPWDataPath = setConfigPackage.ConfigValue;
                        break;

                    case "id_card_check_finger":
                        FaceValidationConfig.Instance.IdCardCheckFinger = ConvertUtil.ToBool(setConfigPackage.ConfigValue, FaceValidationConfig.Instance.IdCardCheckFinger);
                        break;

                    case "id_card_repeat_check":
                        FaceValidationConfig.Instance.IdCardRepeatCheck = ConvertUtil.ToBool(setConfigPackage.ConfigValue, FaceValidationConfig.Instance.IdCardRepeatCheck);
                        break;

                    case "face_match_mode":
                        FaceValidationConfig.Instance.FaceMatchMode = (FaceMatchType)EnumExUtil.CodeToEnum(FaceMatchType.Undefined, setConfigPackage.ConfigValue);
                        switch (FaceValidationConfig.Instance.FaceMatchMode)
                        {
                            case FaceMatchType.Undefined:
                                //普通身份证模式
                                FaceValidationConfig.Instance.NoCardMode = false;
                                break;

                            case FaceMatchType.OneToOne:
                                // 1:1模式,需要身份证,不比较身份证内的照片用导入的照片
                                FaceValidationConfig.Instance.NoCardMode = false;
                                break;

                            case FaceMatchType.OneToN:
                                //1:N模式,该模式无需身份证
                                FaceValidationConfig.Instance.NoCardMode = true;
                                break;

                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                        break;
                    default:
                        throw new Exception("unknow config name:" + setConfigPackage.ConfigName);
                        break;
                }
                DebugLogger.LogDebugMessage($"更新参数:{setConfigPackage.ConfigName} = > {setConfigPackage.ConfigValue}" );
            }

            if (oldCenterUrl != FaceValidationConfig.Instance.CenterServiceUrl)
            {
                PoyaVerifaceClientUtil.Instance.Reset();
            }

            try
            {
                FaceValidationConfig.Instance.WriteConfig();
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            return false;
        }

        //private bool UpdateIPAddress(SetIpAddressPackage setIpAddressPackage)
        //{
        //    string[] Dns = { "127.0.0.1" };
        //    var CurrentInterface = GetActiveEthernetOrWifiNetworkInterface();
        //    if (CurrentInterface == null) return false;

        //    ManagementClass objMC = new ManagementClass("Win32_NetworkAdapterConfiguration");
        //    ManagementObjectCollection objMOC = objMC.GetInstances();
        //    foreach (ManagementObject objMO in objMOC)
        //    {
        //        if ((bool)objMO["IPEnabled"])
        //        {
        //            if (objMO["Caption"].ToString().Contains(CurrentInterface.Description))
        //            {
        //                ManagementBaseObject objdns = objMO.GetMethodParameters("SetDNSServerSearchOrder");
        //                if (objdns != null)
        //                {
        //                    objdns["DNSServerSearchOrder"] = Dns;
        //                    objMO.InvokeMethod("SetDNSServerSearchOrder", objdns, null);
        //                }

        //                ManagementBaseObject setIP;
        //                ManagementBaseObject newIP =
        //                    objMO.GetMethodParameters("EnableStatic");

        //                newIP["IPAddress"] = new string[] { "192.168.0.201" };
        //                newIP["SubnetMask"] = new string[] { "255.255.255.0" };

        //                setIP = objMO.InvokeMethod("EnableStatic", newIP, null);
        //            }
        //        }
        //    }

        //    return true;
        //}

        //private bool UpdateIPAddress3(SetIpAddressPackage setIpAddressPackage)
        //{
        //    string[] Dns = { setIpAddressPackage.DNS1, setIpAddressPackage.DNS2 };
        //    var CurrentInterface = GetActiveEthernetOrWifiNetworkInterface();
        //    if (CurrentInterface == null) return false;

        //    ManagementClass objMC = new ManagementClass("Win32_NetworkAdapterConfiguration");
        //    ManagementObjectCollection objMOC = objMC.GetInstances();
        //    foreach (ManagementObject objMO in objMOC)
        //    {
        //        if ((bool)objMO["IPEnabled"])
        //        {
        //            if (objMO["Caption"].ToString().Contains(CurrentInterface.Description))
        //            {
        //                ManagementBaseObject objdns = objMO.GetMethodParameters("SetDNSServerSearchOrder");
        //                if (objdns != null)
        //                {
        //                    objdns["DNSServerSearchOrder"] = Dns;
        //                    objMO.InvokeMethod("SetDNSServerSearchOrder", objdns, null);
        //                }
        //                ManagementBaseObject newIP = objMO.GetMethodParameters("EnableStatic");
        //                newIP["IPAddress"] = new string[] { setIpAddressPackage .IPAddress};
        //                newIP["SubnetMask"] = new string[] { setIpAddressPackage.SubnetMask };
        //                ManagementBaseObject setIP = objMO.InvokeMethod("EnableStatic", newIP, null);

        //                ManagementBaseObject newGetway  = objMO.GetMethodParameters("SetGateways");
        //                newGetway["DefaultIPGateway"] = new string[] { setIpAddressPackage.Gateways };
        //                newGetway["GatewayCostMetric"] = new int[] { 1 };
        //                objMO.InvokeMethod("SetGateways", newGetway, null);
        //            }
        //        }
        //    }

        //    return true;
        //}

        //public static NetworkInterface GetActiveEthernetOrWifiNetworkInterface()
        //{
        //    var Nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(
        //        a => a.OperationalStatus == OperationalStatus.Up &&
        //             //(a.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || a.NetworkInterfaceType == NetworkInterfaceType.Ethernet) &&
        //             (a.NetworkInterfaceType == NetworkInterfaceType.Ethernet) &&
        //             a.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily.ToString() == "InterNetwork"));

        //    return Nic;
        //}

        //private bool UpdateIPAddress2(SetIpAddressPackage setIpAddressPackage)
        //{
        //    ManagementBaseObject inPar = null;
        //    ManagementBaseObject outPar = null;
        //    ManagementClass mc = new ManagementClass("Win32_NetworkAdapterConfiguration");
        //    ManagementObjectCollection moc = mc.GetInstances();
        //    ManagementObject currentMo = null;

        //    //=============== 1.查找当前的Mo==============
        //    currentMo = FindMo(moc, setIpAddressPackage.CurrentIPAddress);
        //    if (currentMo == null)
        //    {
        //        //如果没有指定IP的,就找一个启用的网卡
        //        currentMo = FindMo(moc, "");
        //    }

        //    if (currentMo == null)
        //    {
        //        return false;
        //    }

        //    LogUtils.Instance.ShowDebugLog("Begin update ip address to" + setIpAddressPackage.IPAddress);
        //    ManagementBaseObject objNewIP, objNewGate, objNewDns;
        //    objNewIP = currentMo.GetMethodParameters("EnableStatic");
        //    objNewGate = currentMo.GetMethodParameters("SetGateways");
        //    objNewDns = currentMo.GetMethodParameters("EnableDNS");

        //    objNewGate["DefaultIPGateway"] = new string[] { setIpAddressPackage.Gateways};
        //    objNewGate["GatewayCostMetric"] = new int[] {1};
        //    objNewIP["IPAddress"] = new string[] { setIpAddressPackage.IPAddress};
        //    objNewIP["SubnetMask"] = new string[] { setIpAddressPackage.SubnetMask};
        //    objNewDns["DNSServerSearchOrder"] = new string[] { setIpAddressPackage.DNS1, setIpAddressPackage.DNS2 };

        //    currentMo.InvokeMethod("SetDNSServerSearchOrder", objNewDns, null);
        //    currentMo.InvokeMethod("EnableStatic", objNewIP, null);
        //    currentMo.InvokeMethod("SetGateways", objNewGate, null);
        //    //currentMo.InvokeMethod("EnableDHCP",null);
        //    //自动dns
        //    //if (!(bool)mo["DHCPEnabled"])
        //    //{
        //    //    inPar = mo.GetMethodParameters("EnableDHCP");
        //    //    outPar = mo.InvokeMethod("EnableDHCP", inPar, null);
        //    //}
        //    //else
        //    //{
        //    //inPar = mo.GetMethodParameters("SetDNSServerSearchOrder");
        //    //inPar["DNSServerSearchOrder"] = DNS; //设置DNS  1.DNS 2.备用DNS  
        //    //mo.InvokeMethod("SetDNSServerSearchOrder", inPar, null);// 执行  
        //    //}

        //    LogUtils.Instance.ShowDebugLog("Update ip address success");
        //    return true;
        //}

        //private ManagementObject FindMo(ManagementObjectCollection moc,string currentIP)
        //{
        //    foreach (ManagementObject mo in moc)
        //    {
        //        //if (mo["Caption"].ToString().IndexOf(FaceValidationConfig.Instance.NetCardName, StringComparison.Ordinal) < 0)
        //        if ((bool) mo["IPEnabled"] != true)
        //        {
        //            continue;
        //        }

        //        if (currentIP != "")
        //        {
        //            bool findIP = false;

        //            string[] ipList = (mo["IPAddress"] as string[]);
        //            foreach (string ip in ipList)
        //            {
        //                if (ip == currentIP)
        //                {
        //                    findIP = true;
        //                    return mo;
        //                }
        //            }

        //            if (!findIP)
        //            {
        //                return mo;
        //            }
        //        }
        //    }

        //    return null;
        //}
    }
}