using NAudio.Wave;
using System.Diagnostics;

namespace WinFormsApp1
{

    public class LTC_Timecode
    {
        byte frame_unites;
        byte user_bits_field1;
        byte frame_tens;
        byte drop_frame_flag;
        byte color_frame_flag;
        byte user_bits_field2;
        byte seconds_unites;
        byte user_bits_field3;
        byte seconds_tens;
        byte flag1;
        byte user_bits_field4;
        byte minutes_unites;
        byte user_bits_field5;
        byte minutes_tens;
        byte flag2;
        byte user_bits_field6;
        byte hour_unites;
        byte user_bits_field7;
        byte hour_tens;
        byte clock_flag;
        byte flag3;
        byte user_bit_filed8;
        byte syncword0;
        byte syncword1;

        public int frame;
        public int second;
        public int mins;
        public int hour;

        byte bits2byte(List<byte> raw)
        {
            byte ret = 0;
            for (int i = raw.Count-1; i >= 0; i--) // little endian
            // for (int i=0;i<raw.Count();i++) big endian
            {
                ret = (byte)((ret << 1) | raw[i]);
            }
            return ret;
        }

        public LTC_Timecode(List<byte> raw)
        {
            frame_unites = bits2byte(raw.GetRange(0, 4));
            user_bits_field1 = bits2byte(raw.GetRange(4, 4));
            frame_tens = bits2byte(raw.GetRange(8, 2));
            drop_frame_flag = bits2byte(raw.GetRange(10, 1));
            color_frame_flag = bits2byte(raw.GetRange(11, 1));
            user_bits_field2 = bits2byte(raw.GetRange(12, 4));

            seconds_unites = bits2byte(raw.GetRange(16, 4));
            user_bits_field3 = bits2byte(raw.GetRange(20, 4));
            seconds_tens = bits2byte(raw.GetRange(24, 3));
            flag1 = bits2byte(raw.GetRange(27, 1));
            user_bits_field4 = bits2byte(raw.GetRange(28, 4));

            minutes_unites = bits2byte(raw.GetRange(32, 4));
            user_bits_field5 = bits2byte(raw.GetRange(36, 4));
            minutes_tens = bits2byte(raw.GetRange(40, 3));
            flag2 = bits2byte(raw.GetRange(43, 1));
            user_bits_field6 = bits2byte(raw.GetRange(44, 4));

            hour_unites = bits2byte(raw.GetRange(48, 4));
            user_bits_field7 = bits2byte(raw.GetRange(52, 4));
            hour_tens = bits2byte(raw.GetRange(56, 2));


            clock_flag = bits2byte(raw.GetRange(58, 1));
            flag3 = bits2byte(raw.GetRange(59, 1));
            user_bit_filed8 = bits2byte(raw.GetRange(60, 4));

            syncword0 = 0x3F;
            syncword1 = 0xFD;

            frame = frame_tens * 10 + frame_unites;
            second = seconds_tens * 10 + seconds_unites;
            mins = minutes_tens * 10 + minutes_unites;
            hour = hour_tens * 10 + hour_unites;
        }
    }

    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void decode_ltc_timecode(List<byte> raw)
        {
            LTC_Timecode ltc = new LTC_Timecode(raw);
            Debug.WriteLine(ltc.hour.ToString() + ":" + ltc.mins.ToString() + ":" + ltc.second.ToString() + ":" + ltc.frame.ToString());
        }

        int pulse_width = 1;
        int last_pulse_cycle = 0;
        bool pulse_reverse = true;
        int cur_rawdata_len = 0;
        List<byte> raw_data = new List<byte>();
        const UInt16 LTC_END = 0x3FFD;
        // get the max width
        int max_pulse_width = -1;

        private bool find_ltc_end()
        {
            int count = raw_data.Count();
            UInt16 cur_end = 0;
            for(int i = count-16;i<count;i++)
            {
                cur_end = (ushort)((cur_end << 1) | raw_data[i]);
            }

            if (cur_end == LTC_END)
                return true;
            else
                return false;
        }

        private bool decode_raw_music_data(Int16 amplitude)
        {
            byte cur_bit = 0;
            int pulse_cycle = 0;
            if (amplitude < 0)
            {
                pulse_cycle = -1;
            }
            else
            {
                pulse_cycle = 1;
            }

            if(pulse_cycle != last_pulse_cycle)
            {
                if(pulse_width > 7)
                {
                    if(pulse_width > 14)
                    {
                        raw_data.Add(0);
                    }
                    else
                    {
                        if(pulse_reverse)
                        {
                            raw_data.Add(1);
                            pulse_reverse = false;
                        }
                        else
                        {
                            pulse_reverse = true;
                        }
                    }

                    if(raw_data.Count() >= 80)
                    {

                        if (find_ltc_end())
                        {
                            //Debug.WriteLine("fidn ltc end");
                            decode_ltc_timecode(raw_data.GetRange(raw_data.Count()-80,80));
                            raw_data.Clear();
                            pulse_width = 1;
                            return true;
                        }
                    }
                }
                pulse_width = 1;
                last_pulse_cycle = pulse_cycle;
            }
            else
            {
                pulse_width += 1;
            }

            max_pulse_width = pulse_width > max_pulse_width ? pulse_width : max_pulse_width;
            return false;
        }


        private void button1_Click(object sender, EventArgs e)
        {
            var outputFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "NAudio");
            Directory.CreateDirectory(outputFolder);
            var outputFilePath = Path.Combine(outputFolder, "recorded.wav");

            var waveIn = new WaveInEvent_Dmd();//new WaveInEvent();
            // defalt 0 is Focusrite Audio
            // 1 is WF-1000XM4 Audio
            waveIn.DeviceNumber = 0;
            WaveFileWriter writer = null;

            bool closing = false;
            var f = new Form();
            var buttonRecord = new Button() { Text = "Record" };
            var buttonStop = new Button() { Text = "Stop", Left = buttonRecord.Right, Enabled = false };
            f.Controls.AddRange(new Control[] { buttonRecord, buttonStop });
            buttonRecord.Click += (s, a) =>
            {
                writer = new WaveFileWriter(outputFilePath, waveIn.WaveFormat);
                waveIn.StartRecording();
                buttonRecord.Enabled = false;
                buttonStop.Enabled = true;
            };

            waveIn.DataAvailable += (s, a) =>
            {
                //writer.Write(a.Buffer, 0, a.BytesRecorded);
                //Debug.WriteLine(a.BytesRecorded.ToString());
                for (int i = 0; i < a.BytesRecorded; i+=2)
                {
                    if(decode_raw_music_data((Int16)(a.Buffer[i] | (a.Buffer[i+1] << 8))))
                    {
                        //break;
                    }
                }
            };

            buttonStop.Click += (s, a) => waveIn.StopRecording();
            f.FormClosing += (s, a) => { closing = true; waveIn.StopRecording(); };

            waveIn.RecordingStopped += (s, a) =>
            {
                writer?.Dispose();
                writer = null;
                buttonRecord.Enabled = true;
                buttonStop.Enabled = false;
                if (closing)
                {
                    waveIn.Dispose();
                }
            };

            f.ShowDialog();
        }
    }
}