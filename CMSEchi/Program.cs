using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CMSEchi
{
    public enum Loglevel
    {
        Info,
        Trace,
        Warn,
        Error
    }
    class Program
    {
        private static IReadOnlyList<CmsModel> config { get; set; }

        // Logging errors in file
        public static void LogFile(Enum loglevel, string messageText)
        {
            string date = DateTime.Now.ToString("yyyy-MM-dd");
            string logdir = AppDomain.CurrentDomain.BaseDirectory;
            StringBuilder log = new StringBuilder();
            log.Append(DateTime.Now.ToString(CultureInfo.InvariantCulture))
                .Append($"| {loglevel} |")
                .Append(messageText)
                .Append("\n");
            try
            {
                string filename = $"{logdir}error_{date}.log";
                File.AppendAllText(filename, log.ToString());
            }
            catch (Exception e)
            {
                if (e.StackTrace != null) LogFile(Loglevel.Error, e.Message.ToString() + "\n" + e.StackTrace.ToString());
            }
        }
        
        // Reading the .json file to get information about the CMS ECHI file format
        private static void ReadConfig()
        {
            try
            {
                string file = $"{AppDomain.CurrentDomain.BaseDirectory}appsettings.json";
                using (StreamReader r = new StreamReader(file))
                {
                    string jsonData = File.ReadAllText(file);
                    config = JsonSerializer.Deserialize<IReadOnlyList<CmsModel>>(jsonData);
                }
            }
            catch (Exception e)
            {
                LogFile(Loglevel.Error,e.Message + "\n" + e.StackTrace);
            }
        }
        
        // Get a list of files to decode
        private static string[] EchiFilesList(string path)
        {
            try
            {
                string[] echiFilesList = Directory.GetFiles($"{path}", "chr*");
                string[] echiFileNames = new string[echiFilesList.Length];
                for (int i=0; i<echiFilesList.Length; i++)
                {
                    echiFileNames[i] = Path.GetFileName(echiFilesList[i]);
                }
                return echiFileNames;
            }
            catch (Exception e)
            {
                LogFile(Loglevel.Error,e.Message + "\n" + e.StackTrace);
            }
            return null;
        }
        
        // Reading the ECHI file header, displaying the CMS version and file serial number
        private static int[] EchiFilesHead(string echiFilesList)
        {
            int[] fileHead = new int[2]; 
            byte[] versionB = new byte[4];
            byte[] sequenceNumberB = new byte[4];
            
            try
            {
                FileStream fs = new FileStream(echiFilesList, FileMode.Open, FileAccess.Read);
                fs.Read(versionB, 0, 4);
                fs.Read(sequenceNumberB, 0, 4);
                fs.Close();

                fileHead[0] = BitConverter.ToInt32(versionB);
                fileHead[1] = BitConverter.ToInt32(sequenceNumberB);
                return fileHead;
            }
            catch (Exception e)
            {
                LogFile(Loglevel.Error,e.Message + "\n" + e.StackTrace);            
            }
            return null;
        }
        
        
        // We output the decrypted data to a text file with a delimiter in the form of ","
        private static void DataExport(int fileHead, string outdir, string echiFile, string heads, StringBuilder data)
        {
            string file = $"{echiFile}_{fileHead}.txt";
            try
            {
                if (!Directory.Exists(outdir))
                {
                    Directory.CreateDirectory(outdir);
                }
                if (!File.Exists($"{outdir}/{file}"))
                {
                    using (StreamWriter fs = File.AppendText($"{outdir}/{file}"))
                    {
                        fs.WriteLine(heads);
                        fs.WriteLine(data);
                        fs.Close();
                    }
                }
            }
            catch (Exception e)
            {
                LogFile(Loglevel.Error,e.Message + "\n" + e.StackTrace);            
            }
        }

        static void Main(string[] args)
        {
            Console.WriteLine("---=== CMS ECHI files parser ===---");
            Console.WriteLine("-------------------------------------");
            // Checking program arguments
            if (args == null || args.Length == 0)
            {
                Console.WriteLine("Для запуска программы необходимо указать аргументы.");
                Console.WriteLine("Пример: CMSEchi <cms echi files dir> <decoded output dir>");
                Console.WriteLine("     <cms echi files dir> - директория, где находятся файлы Avaya CMS ECHI,");
                Console.WriteLine("     <decoded output dir> - директория для вывода декодированных файлов.");
                Environment.Exit(0);
            }

            ReadConfig(); // Reading the .json file
            
            string[] filesList = EchiFilesList(args[0]); // Get a list of CMS ECHI files to decode
            if (filesList == null || filesList.Length == 0)
            {
                LogFile(Loglevel.Info,$"Прочитано - 0 файла(ов).");
                Environment.Exit(0);
            }
            LogFile(Loglevel.Info,$"Прочитано - {filesList.Length} файла(ов).");

            // Read data from file and decode it
            try
            {
                StringBuilder result = new StringBuilder(); // create a new string constructor where we put the decoded data
                for (int i = 0; i < filesList.Length; i++)
                {
                    int[] fileVersion = EchiFilesHead($"{args[0]}/{filesList[i]}"); // read the CMS version in the file header
                    // looking for data to decode according to the CMS version in the .json file
                    var configData = config.First(s => s.version == fileVersion[0]); 

                    if (true)
                    {
                        // array of fields given
                        int[] fieldsLength = Array.ConvertAll(configData.fieldsLength.Split(','), int.Parse);
                        LogFile(Loglevel.Info,$"файл - {filesList[i]}, версия {configData.version}, длина полей - {fieldsLength.Length}, сумма полей - {fieldsLength.Sum()}.");
                        
                        var fs = File.Open($"{args[0]}/{filesList[i]}", FileMode.Open);
                        byte[] data = new byte[fs.Length];
                        using (BinaryReader reader = new BinaryReader(fs))
                        {
                            // we start reading data from a file from 8 bytes
                            // because the first 8 bytes in each file are used for the header = CMS version + file serial number
                            reader.BaseStream.Seek(8, SeekOrigin.Begin);
                            reader.Read(data, 0, Convert.ToInt32(fs.Length));
                            result.Clear();
                            // we cut the received array of data into pancake lines ??? byte. ??? - byte length is taken from .json file
                            for (int j = 0; j < fs.Length - 8; j = j + configData.lineLength)
                            {
                                // we cut the read data array into lines by ??? byte length. ??? - byte length is taken from .json file
                                byte[] line = data.Skip(j).Take(configData.lineLength).ToArray(); 

                                // cut the string into component parts according to the length of the data fields
                                int position = 0; // field length position in field array
                                int skipCount = 0; // how many bytes skip to get data
                                while (position < fieldsLength.Length) // until you reach the end of the field array
                                {
                                    byte[] decode = line.Skip(skipCount).Take(fieldsLength[position]).ToArray();
                                    if (position == configData.bitsIndex) // if position = bit data, then decompose 1 byte into 8 bits.
                                    {
                                        var bits = new BitArray(decode);
                                        for (int m = 0; m < bits.Length; m++)
                                            result.Append(Convert.ToInt16(bits[m])).Append(",");
                                    }
                                    else if (position == configData.bitsIndex + 1) // if position = bit data +1, then take 1 bit from this data
                                    {
                                        var bits = new BitArray(decode);
                                        result.Append(Convert.ToInt16(bits[0])).Append(",");
                                    }
                                    else
                                    {
                                        switch (fieldsLength[position]) // decode data depending on position and format
                                        {
                                            case 1:
                                                result.Append(Convert.ToInt16(decode[0])).Append(",");
                                                break;
                                            case 2:
                                                result.Append(BitConverter.ToInt16(decode)).Append(",");
                                                break;
                                            case 4:
                                                result.Append(BitConverter.ToInt32(decode)).Append(",");
                                                break;
                                            case 3:
                                            case 10:
                                            case 17:
                                            case 16:
                                            case 21:
                                            case 25:
                                            case 97:
                                                result.Append("\"").Append(Array.ConvertAll(decode, Convert.ToChar))
                                                    .Append("\",");
                                                break;
                                        }
                                    }

                                    skipCount += fieldsLength[position];
                                    position++;
                                }

                                result.Append("\n");
                            }
                        }
                        // Uploading decoded data to a text file
                        DataExport(fileVersion[1],args[1],filesList[i],configData.heads,result);
                        fs.Close();
                    }
                }
            }
            catch (Exception e)
            {
                LogFile(Loglevel.Error,e.Message + "\n" + e.StackTrace);
            }
            Console.WriteLine("---=== CMS ECHI files parser END! ===---");
        }
    }
}