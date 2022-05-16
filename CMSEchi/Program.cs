using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CMSEchi
{
    class Program
    {
        // Читаем файл .json для получения данных о формате файла CMS ECHI
        private static IReadOnlyList<CmsModel> config { get; set; }

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
                Console.WriteLine($"ERROR: {e}");
            }
        }
        
        // Получаем список файлов для декодирования
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
                Console.WriteLine($"ERROR: {e}");
            }
            return null;
        }
        
        // Читаем заголовок файла ECHI, выводим версию CMS и порядковый номер файла
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
                Console.WriteLine($"ERROR: {e}");
            }
            return null;
        }
        
        
        // Выводим расшифрованные данные в текстовый файл с разделителем в виде ","
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
                Console.WriteLine($"ERROR: {e}");
            }
        }

        static void Main(string[] args)
        {
            Console.WriteLine("---=== CMS ECHI files parser ===---");
            Console.WriteLine("-------------------------------------");
            // Проверяем аргументы программы
            if (args == null || args.Length == 0)
            {
                Console.WriteLine("Для запуска программы необходимо указать аргументы.");
                Console.WriteLine("Пример: CMSEchi <cms echi files dir> <decoded output dir>");
                Console.WriteLine("     <cms echi files dir> - директория, где находятся файлы Avaya CMS ECHI,");
                Console.WriteLine("     <decoded output dir> - директория для вывода декодированных файлов.");
                Environment.Exit(0);
            }

            ReadConfig(); // Читаем файл .json
            
            string[] filesList = EchiFilesList(args[0]); // Получаем список файлов CMS ECHI для декодирования
            Console.WriteLine($"Прочитано - {filesList.Length} файла(ов).");

            // Читаем данные из файле и декодируем их
            try
            {
                StringBuilder result = new StringBuilder(); // создаем новый конструктор строки, куда поместим декодированные данные
                for (int i = 0; i < filesList.Length; i++)
                {
                    int[] fileVersion = EchiFilesHead($"{args[0]}/{filesList[i]}"); // читаем в заголовке файла версию CMS
                    var configData = config.First(s => s.version == fileVersion[0]); // ищем данные для декодирования по версии CMS в файле .json

                    if (true)
                    {
                        // массиве полей данный
                        int[] fieldsLength = Array.ConvertAll(configData.fieldsLength.Split(','), int.Parse);
                        
                        var fs = File.Open($"{args[0]}/{filesList[i]}", FileMode.Open);
                        byte[] data = new byte[fs.Length];
                        using (BinaryReader reader = new BinaryReader(fs))
                        {
                            // начинаем читать данные из файла с 8 байта
                            // т.к. первые 8 байт в каждом файле заняты под заголовок = версия CMS + порядковый номер файла
                            reader.BaseStream.Seek(8, SeekOrigin.Begin);
                            reader.Read(data, 0, Convert.ToInt32(fs.Length));
                            result.Clear();
                            // нарезаем полученный массив данных на строки блиной ??? байт. ??? - длина байт берется из файла .json
                            for (int j = 0; j < fs.Length - 8; j = j + configData.lineLength)
                            {
                                // режем прочитанный массив данных на строки по ??? байт длиной. ??? - длина байт берется из файла .json
                                byte[] line = data.Skip(j).Take(configData.lineLength).ToArray(); 

                                // режем строку на составляющие части по длине полей данных
                                int position = 0; // позиция длины поля в массиве полей
                                int skipCount = 0; // сколько байт пропускаем для получения данных
                                while (position < fieldsLength.Length) // пока не достигли конца массива полей
                                {
                                    byte[] decode = line.Skip(skipCount).Take(fieldsLength[position]).ToArray();
                                    if (position == configData.bitsIndex) // если позиция = битовым данным, то раскладываем 1 байт на 8 бит.
                                    {
                                        var bits = new BitArray(decode);
                                        for (int m = 0; m < bits.Length; m++)
                                            result.Append(Convert.ToInt16(bits[m])).Append(",");
                                    }
                                    else if (position == configData.bitsIndex + 1) // если позиция = битовым данным +1, то берем 1 бит из этих данных
                                    {
                                        var bits = new BitArray(decode);
                                        result.Append(Convert.ToInt16(bits[0])).Append(",");
                                    }
                                    else
                                    {
                                        switch (fieldsLength[position]) // декодируем данные в зависимости от позиции и формата
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
                        // Выгружаем декодированные данные в текстовый файл
                        DataExport(fileVersion[1],args[1],filesList[i],configData.heads,result);
                        fs.Close();
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"ERROR: {e}");
            }
            Console.WriteLine("---=== CMS ECHI files parser END! ===---");
        }
    }
}