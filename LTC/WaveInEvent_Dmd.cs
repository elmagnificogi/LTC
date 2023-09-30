using System;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Mixer;

namespace NAudio.Wave
{
    //
    // ժҪ:
    //     Recording using waveIn api with event callbacks. Use this for recording in non-gui
    //     applications Events are raised as recorded buffers are made available
    public class WaveInEvent_Dmd : IWaveIn, IDisposable
    {
        private readonly AutoResetEvent callbackEvent;

        private readonly SynchronizationContext syncContext;

        private IntPtr waveInHandle;

        private volatile CaptureState captureState;

        private WaveInBuffer[] buffers;

        //
        // ժҪ:
        //     Returns the number of Wave In devices available in the system
        public static int DeviceCount => WaveInterop.waveInGetNumDevs();

        //
        // ժҪ:
        //     Milliseconds for the buffer. Recommended value is 100ms
        public int BufferMilliseconds { get; set; }

        //
        // ժҪ:
        //     Number of Buffers to use (usually 2 or 3)
        public int NumberOfBuffers { get; set; }

        //
        // ժҪ:
        //     The device number to use
        public int DeviceNumber { get; set; }

        //
        // ժҪ:
        //     WaveFormat we are recording in
        public WaveFormat WaveFormat { get; set; }

        //
        // ժҪ:
        //     Indicates recorded data is available
        public event EventHandler<WaveInEventArgs> DataAvailable;

        //
        // ժҪ:
        //     Indicates that all recorded data has now been received.
        public event EventHandler<StoppedEventArgs> RecordingStopped;

        //
        // ժҪ:
        //     Prepares a Wave input device for recording
        public WaveInEvent_Dmd()
        {
            callbackEvent = new AutoResetEvent(initialState: false);
            syncContext = SynchronizationContext.Current;
            DeviceNumber = 0;
            WaveFormat = new WaveFormat(48000, 16, 1);
            BufferMilliseconds = 100;
            NumberOfBuffers = 3;
            captureState = CaptureState.Stopped;
        }

        //
        // ժҪ:
        //     Retrieves the capabilities of a waveIn device
        //
        // ����:
        //   devNumber:
        //     Device to test
        //
        // ���ؽ��:
        //     The WaveIn device capabilities
        public static WaveInCapabilities GetCapabilities(int devNumber)
        {
            WaveInCapabilities waveInCaps = default(WaveInCapabilities);
            int waveInCapsSize = Marshal.SizeOf(waveInCaps);
            MmException.Try(WaveInterop.waveInGetDevCaps((IntPtr)devNumber, out waveInCaps, waveInCapsSize), "waveInGetDevCaps");
            return waveInCaps;
        }

        private void CreateBuffers()
        {
            int num = BufferMilliseconds * WaveFormat.AverageBytesPerSecond / 1000;
            if (num % WaveFormat.BlockAlign != 0)
            {
                num -= num % WaveFormat.BlockAlign;
            }

            buffers = new WaveInBuffer[NumberOfBuffers];
            for (int i = 0; i < buffers.Length; i++)
            {
                buffers[i] = new WaveInBuffer(waveInHandle, num);
            }
        }

        private void OpenWaveInDevice()
        {
            CloseWaveInDevice();
            MmException.Try(WaveInterop.waveInOpenWindow(out waveInHandle, (IntPtr)DeviceNumber, WaveFormat, callbackEvent.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, WaveInterop.WaveInOutOpenFlags.CallbackEvent), "waveInOpen");
            CreateBuffers();
        }

        //
        // ժҪ:
        //     Start recording
        public void StartRecording()
        {
            if (captureState != 0)
            {
                throw new InvalidOperationException("Already recording");
            }

            OpenWaveInDevice();
            MmException.Try(WaveInterop.waveInStart(waveInHandle), "waveInStart");
            captureState = CaptureState.Starting;
            ThreadPool.QueueUserWorkItem(delegate
            {
                RecordThread();
            }, null);
        }

        private void RecordThread()
        {
            Exception e = null;
            try
            {
                DoRecording();
            }
            catch (Exception ex)
            {
                e = ex;
            }
            finally
            {
                captureState = CaptureState.Stopped;
                RaiseRecordingStoppedEvent(e);
            }
        }

        private void DoRecording()
        {
            captureState = CaptureState.Capturing;
            WaveInBuffer[] array = buffers;
            foreach (WaveInBuffer waveInBuffer in array)
            {
                if (!waveInBuffer.InQueue)
                {
                    waveInBuffer.Reuse();
                }
            }

            while (captureState == CaptureState.Capturing)
            {
                if (!callbackEvent.WaitOne())
                {
                    continue;
                }

                array = buffers;
                foreach (WaveInBuffer waveInBuffer2 in array)
                {
                    if (waveInBuffer2.Done)
                    {
                        if (waveInBuffer2.BytesRecorded > 0)
                        {
                            this.DataAvailable?.Invoke(this, new WaveInEventArgs(waveInBuffer2.Data, waveInBuffer2.BytesRecorded));
                        }

                        if (captureState == CaptureState.Capturing)
                        {
                            waveInBuffer2.Reuse();
                        }
                    }
                }
            }
        }

        private void RaiseRecordingStoppedEvent(Exception e)
        {
            EventHandler<StoppedEventArgs> handler = this.RecordingStopped;
            if (handler == null)
            {
                return;
            }

            if (syncContext == null)
            {
                handler(this, new StoppedEventArgs(e));
                return;
            }

            syncContext.Post(delegate
            {
                handler(this, new StoppedEventArgs(e));
            }, null);
        }

        //
        // ժҪ:
        //     Stop recording
        public void StopRecording()
        {
            if (captureState != 0)
            {
                captureState = CaptureState.Stopping;
                MmException.Try(WaveInterop.waveInStop(waveInHandle), "waveInStop");
                MmException.Try(WaveInterop.waveInReset(waveInHandle), "waveInReset");
                callbackEvent.Set();
            }
        }

        //
        // ժҪ:
        //     Gets the current position in bytes from the wave input device. it calls directly
        //     into waveInGetPosition)
        //
        // ���ؽ��:
        //     Position in bytes
        public MmTime GetPosition()
        {
            MmTime mmTime = default(MmTime);
            mmTime.wType = 32u;
            MmException.Try(WaveInterop.waveInGetPosition(waveInHandle, out mmTime, Marshal.SizeOf(mmTime)), "waveInGetPosition");
            if (mmTime.wType != 32)
            {
                throw new Exception($"waveInGetPosition: wType -> Expected {4}, Received {mmTime.wType}");
            }

            return mmTime;
        }

        //
        // ժҪ:
        //     Dispose pattern
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (captureState != 0)
                {
                    StopRecording();
                }

                CloseWaveInDevice();
            }
        }

        private void CloseWaveInDevice()
        {
            WaveInterop.waveInReset(waveInHandle);
            if (buffers != null)
            {
                for (int i = 0; i < buffers.Length; i++)
                {
                    buffers[i].Dispose();
                }

                buffers = null;
            }

            WaveInterop.waveInClose(waveInHandle);
            waveInHandle = IntPtr.Zero;
        }

        //
        // ժҪ:
        //     Microphone Level
        public MixerLine GetMixerLine()
        {
            if (waveInHandle != IntPtr.Zero)
            {
                return new MixerLine(waveInHandle, 0, MixerFlags.WaveInHandle);
            }

            return new MixerLine((IntPtr)DeviceNumber, 0, MixerFlags.WaveIn);
        }

        //
        // ժҪ:
        //     Dispose method
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}