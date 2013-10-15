﻿using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace shop
{
    public class shopConfig
    {
        public int bloodmoon = 5;
        public int eclipse = 10;
        public int day = 0;
        public int night = 0;
        public static shopConfig Read(string path)
        {
            if (!File.Exists(path))
                return new shopConfig();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return Read(fs);
            }
        }
        public static shopConfig Read(Stream stream)
        {
            using (var sr = new StreamReader(stream))
            {
                var cf = JsonConvert.DeserializeObject<shopConfig>(sr.ReadToEnd());
                if (ConfigRead != null)
                    ConfigRead(cf);
                return cf;
            }
        }

        public void Write(string path)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Write))
            {
                Write(fs);
            }
        }

        public void Write(Stream stream)
        {
            var str = JsonConvert.SerializeObject(this, Formatting.Indented);
            using (var sw = new StreamWriter(stream))
            {
                sw.Write(str);
            }
        }
        public static Action<shopConfig> ConfigRead;
    }
}