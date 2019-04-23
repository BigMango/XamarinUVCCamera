using System;
using System.Collections.Generic;
using System.IO;
using Android.App;
using Android.Graphics;
using Android.Hardware.Usb;
using Android.OS;
using Android.Support.V7.App;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using Com.Serenegiant.Usb;
using Com.Serenegiant.Widget;
using Java.IO;
using Java.Lang;
using Java.Nio;
using Object = System.Object;
using Size = Com.Serenegiant.Usb.Size;

namespace AppUVCCamera
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme", MainLauncher = true)]
    public class MainActivity : Activity, USBMonitor.IOnDeviceConnectListener, TextureView.ISurfaceTextureListener, IFrameCallback
    {

        private USBMonitor mUSBMonitor;
        private UVCCamera mUVCCamera;
        private TextureView mTextureView;
        private SurfaceTexture mSurfaceTexture;

        private Object mSync = new Object();
        private bool isActive, isPreview ;

        private static string TAG = "MainActivity";


        class MyHandler : Handler
        {
            public override void HandleMessage(Message msg)
            {
                base.HandleMessage(msg);
            }
        }

        MyHandler handler = new MyHandler();

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Xamarin.Essentials.Platform.Init(this, savedInstanceState);
            // Set our view from the "main" layout resource
            SetContentView(Resource.Layout.activity_main);

            mUSBMonitor = new USBMonitor(this, this);//创建

            mTextureView = (TextureView)FindViewById(Resource.Id.camera_surface_view);

            mTextureView.SurfaceTextureListener = this;
            mTextureView.Rotation = 90;//旋转90度

            mTextureView.Click += MTextureViewOnClick;




        }

        private void MTextureViewOnClick(object sender, EventArgs e)
        {
            if (mUVCCamera == null)
            {
                // XXX calling CameraDialog.showDialog is necessary at only first time(only when app has no permission).
                CameraDialog.ShowDialog(this);
            }
            else
            {
                lock(mSync) {
                    mUVCCamera.Destroy();
                    mUVCCamera = null;
                    isActive = isPreview = false;
                }
            }
        }


        protected override void OnStart()
        {
            base.OnStart();
            Log.Verbose(TAG, "onStart:");
            //注意此处的注册和反注册  注册后会有相机usb设备的回调
            lock (mSync)
            {
                if (mUSBMonitor != null)
                {
                    mUSBMonitor.Register();
                }
            }
        }

        protected override void OnStop()
        {
         

            Log.Verbose(TAG, "onStop:");
            lock(mSync) 
            {
                if (mUSBMonitor != null)
                {
                    mUSBMonitor.Unregister();
                }
            }
            base.OnStop();
        }

        protected override void OnDestroy()
        {
            Log.Verbose(TAG, "onDestroy:");
            lock(mSync) {
                isActive = isPreview = false;
                if (mUVCCamera != null)
                {
                    mUVCCamera.Destroy();
                    mUVCCamera.Close();
                    mUVCCamera = null;
                }
                if (mUSBMonitor != null)
                {
                    mUSBMonitor.Destroy();
                    mUSBMonitor = null;
                }
            }
            base.OnDestroy();
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Android.Content.PM.Permission[] grantResults)
        {
            Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);

            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }

        public void OnAttach(UsbDevice device)
        {
            Log.Verbose(TAG, "onAttach:");
            Toast.MakeText(this, "USB_DEVICE_ATTACHED", ToastLength.Short).Show();

            //			 ctrlBlock = new UsbControlBlock(mUSBMonitor, device);
            //	ctrlBlock = mCtrlBlocks.get(device);
            //	if (ctrlBlock == null) {
            //		ctrlBlock = new UsbControlBlock(USBMonitor.this, device);
            //		mCtrlBlocks.put(device, ctrlBlock);
            //		createNew = true;
            //	} else {
            //		createNew = false;
            //	}
            //	if (mOnDeviceConnectListener != null) {
            //		mOnDeviceConnectListener.onConnect(device, ctrlBlock, createNew);
            //	}
            if ((device.DeviceClass == UsbClass.Misc) && (device.DeviceSubclass == UsbClass.Comm))
            {
                mUSBMonitor.RequestPermission(device);
            }
        }

        public void OnCancel(UsbDevice p0)
        {
        }

        Size previewSize;

        public void OnConnect(UsbDevice p0, USBMonitor.UsbControlBlock ctrlBlock, bool p2)
        {
            Log.Verbose(TAG, "onConnect:");
            lock (mSync)
            {
                if (mUVCCamera != null)
                {
                    mUVCCamera.Destroy();
                }

                isActive = isPreview = false;
            }

            handler.Post(delegate
            {
                lock (mSync)
                {
                    UVCCamera camera = new UVCCamera();
                    Log.Verbose(TAG, "创建相机完成时间:" + Java.Lang.JavaSystem.CurrentTimeMillis());

                    int venderId = ctrlBlock.VenderId;
                    int productId = ctrlBlock.ProductId;
                    int fileDescriptor = ctrlBlock.FileDescriptor;
                    int busNum = ctrlBlock.BusNum;
                    int devAddr = ctrlBlock.DevNum;

                    camera.Open(ctrlBlock);
                    Log.Info(TAG, "supportedSize:" + camera.SupportedSize);
                    try
                    {
                        //设置预览尺寸 根据设备自行设置
                        //                            camera.setPreviewSize(UVCCamera.DEFAULT_PREVIEW_WIDTH, UVCCamera.DEFAULT_PREVIEW_HEIGHT, UVCCamera.FRAME_FORMAT_MJPEG);
                    }
                    catch (IllegalArgumentException e)
                    {
                        try
                        {
                            // fallback to YUV mode
                            //设置预览尺寸 根据设备自行设置
                            //                                camera.setPreviewSize(UVCCamera.DEFAULT_PREVIEW_WIDTH, UVCCamera.DEFAULT_PREVIEW_HEIGHT, UVCCamera.DEFAULT_PREVIEW_MODE);
                        }
                        catch (IllegalArgumentException e1)
                        {
                            camera.Destroy();
                            return;
                        }
                    }

                    //                        mPreviewSurface = mUVCCameraView.getHolder().getSurface();//使用Surfaceview的接口
                    if (mSurfaceTexture != null)
                    {
                        isActive = true;
                        //                            camera.setPreviewDisplay(mPreviewSurface);//使用Surfaceview的接口
                        camera.SetPreviewTexture(mSurfaceTexture);
                        Log.Verbose(TAG, "设置相机参数准备启动预览时间:" + Java.Lang.JavaSystem.CurrentTimeMillis().ToString());
                        camera.StartPreview();
                        Log.Verbose(TAG, "设置相机参数准备启动预览完成时间:" + Java.Lang.JavaSystem.CurrentTimeMillis());

                        IList<Size> supportedSizeList = camera.SupportedSizeList;
                        //System.out.println("size个数" + supportedSizeList.size());
                        //for (Size s : supportedSizeList)
                        //{
                        //    System.out.println("size=" + s.width + "***" + s.height);
                        //}
                        //                            camera.setFrameCallback(iFrameCallback, UVCCamera.PIXEL_FORMAT_YUV420SP);//设置回调 和回调数据类型
                        //                            设置预览尺寸 根据设备自行设置
                        //                            camera.setPreviewSize(640, 480);
                        previewSize = camera.PreviewSize;
                        isPreview = true;

                    }

                    lock (mSync)
                    {
                        mUVCCamera = camera;
                    }
                }
            });
        }




        public void OnDettach(UsbDevice p0)
        {
            Log.Verbose(TAG, "onDettach:");
            Toast.MakeText(this, "USB_DEVICE_DETACHED", ToastLength.Short).Show();
        }

        public void OnDisconnect(UsbDevice p0, USBMonitor.UsbControlBlock p1)
        {
        }

        public void OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
        {
            mSurfaceTexture = surface;
            Log.Info(TAG, "onSurfaceTextureAvailable:==" + mSurfaceTexture);
        }

        public bool OnSurfaceTextureDestroyed(SurfaceTexture surface)
        {
            Log.Info(TAG, "onSurfaceTextureDestroyed :  ");
            if (mUVCCamera != null)
            {
                mUVCCamera.StopPreview();
            }
            mSurfaceTexture = null;
            return true;
        }

        public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height)
        {
            Log.Info(TAG, "onSurfaceTextureSizeChanged :  width==" + width + "height=" + height);
        }

        public void OnSurfaceTextureUpdated(SurfaceTexture surface)
        {
           
        }

        public void OnFrame(ByteBuffer p0)
        {
            Log.Verbose(TAG, "相机帧数回调时间:" + Java.Lang.JavaSystem.CurrentTimeMillis());
            //byte[] array = p0.array();//nv21 数据
            //            Bitmap bitmap = YUV420SPDataToBitmap(array, previewSize.width, previewSize.height);//转换bitmap
            //System.out.println("帧数回调=width" + previewSize.width + " height=" + previewSize.height);
        }

        /**
    * YUV420sp原始预览数据转 bitmap
    *
    * @param bytes
    * @param w
    * @param h
    * @return
    */
        public static Bitmap YUV420SPDataToBitmap(byte[] bytes, int w, int h)
        {
            BitmapFactory.Options newOpts = new BitmapFactory.Options();
            newOpts.InJustDecodeBounds = true;
            YuvImage yuvimage = new YuvImage(bytes, ImageFormatType.Nv21, w, h, null);

            byte[] rawImage;
            using (MemoryStream baos = new MemoryStream())
            {
                yuvimage.CompressToJpeg(new Rect(0, 0, w, h), 100, baos); // 80--JPG图片的质量[0-100],100最高
                rawImage = new byte[baos.Length];
                baos.Position = 0;
                baos.Write(rawImage, 0, rawImage.Length);
            }

            //            将rawImage转换成bitmap
            BitmapFactory.Options options = new BitmapFactory.Options();
            options.InPreferredConfig = Bitmap.Config.Rgb565;
            Bitmap bitmap = BitmapFactory.DecodeByteArray(rawImage, 0, rawImage.Length, options);
            return bitmap;
        }
    }
}