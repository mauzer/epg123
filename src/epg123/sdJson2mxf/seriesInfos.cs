﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using epg123.MxfXml;

namespace epg123
{
    public static partial class sdJson2mxf
    {
        private static List<string> seriesDescriptionQueue = new List<string>();
        private static ConcurrentDictionary<string, sdGenericDescriptions> seriesDescriptionResponses = new ConcurrentDictionary<string, sdGenericDescriptions>();

        private static bool buildAllGenericSeriesInfoDescriptions()
        {
            // reset counters
            processedObjects = 0;
            Logger.WriteMessage(string.Format("Entering buildAllGenericSeriesInfoDescriptions() for {0} series.",
                                totalObjects = sdMxf.With[0].SeriesInfos.Count));
            ++processStage; reportProgress();

            // fill mxf programs with cached values and queue the rest
            foreach (MxfSeriesInfo series in sdMxf.With[0].SeriesInfos)
            {
                // sports events will not have a generic description
                if (series.tmsSeriesId.StartsWith("SP"))
                {
                    ++processedObjects; reportProgress();
                    continue;
                }

                // import the cached description if exists, otherwise queue it up
                string seriesId = $"SH{series.tmsSeriesId}0000";
                string filepath = $"{Helper.Epg123CacheFolder}\\{seriesId}";
                FileInfo file = new FileInfo(filepath);
                if (file.Exists && (file.Length > 0) && !epgCache.JsonFiles.ContainsKey(seriesId))
                {
                    using (StreamReader reader = File.OpenText(filepath))
                    {
                        epgCache.AddAsset(seriesId, reader.ReadToEnd());
                    }
                }

                if (epgCache.JsonFiles.ContainsKey(seriesId))
                {
                    try
                    {
                        using (StringReader reader = new StringReader(epgCache.GetAsset(seriesId)))
                        {
                            JsonSerializer serializer = new JsonSerializer();
                            sdGenericDescriptions cached = (sdGenericDescriptions)serializer.Deserialize(reader, typeof(sdGenericDescriptions));
                            if (cached.Code == 0)
                            {
                                series.ShortDescription = cached.Description100;
                                series.Description = cached.Description1000;
                                if (!string.IsNullOrEmpty(cached.StartAirdate))
                                {
                                    series.StartAirdate = cached.StartAirdate;
                                }
                            }
                        }
                        ++processedObjects; reportProgress();
                    }
                    catch
                    {
                        if (int.TryParse(series.tmsSeriesId, out int dummy))
                        {
                            // must use EP to query generic series description
                            seriesDescriptionQueue.Add($"EP{series.tmsSeriesId}0000");
                        }
                        else
                        {
                            ++processedObjects; reportProgress();
                        }
                    }
                }
                else if (!int.TryParse(series.tmsSeriesId, out int dummy))
                {
                    ++processedObjects; reportProgress();
                }
                else
                {
                    // must use EP to query generic series description
                    seriesDescriptionQueue.Add($"EP{series.tmsSeriesId}0000");
                }
            }
            Logger.WriteVerbose($"Found {processedObjects} cached series descriptions.");

            // maximum 500 queries at a time
            if (seriesDescriptionQueue.Count > 0)
            {
                Parallel.For(0, (seriesDescriptionQueue.Count / MAXIMGQUERIES + 1), new ParallelOptions { MaxDegreeOfParallelism = MAXPARALLELDOWNLOADS }, i =>
                {
                    downloadGenericSeriesDescriptions(i * MAXIMGQUERIES);
                });

                processSeriesDescriptionsResponses();
                if (processedObjects != totalObjects)
                {
                    Logger.WriteInformation("Problem occurred during buildGenericSeriesInfoDescriptions(). Did not process all series descriptions.");
                }
            }
            Logger.WriteInformation($"Processed {processedObjects} series descriptions.");
            Logger.WriteMessage("Exiting buildAllGenericSeriesInfoDescriptions(). SUCCESS.");
            seriesDescriptionQueue = null; seriesDescriptionResponses = null;
            return true;
        }

        private static void downloadGenericSeriesDescriptions(int start = 0)
        {
            // build the array of series to request descriptions for
            string[] series = new string[Math.Min(seriesDescriptionQueue.Count - start, MAXIMGQUERIES)];
            for (int i = 0; i < series.Length; ++i)
            {
                series[i] = seriesDescriptionQueue[start + i];
            }

            // request descriptions from Schedules Direct
            Dictionary<string, sdGenericDescriptions> responses = sdAPI.sdGetProgramGenericDescription(series);
            if (responses != null)
            {
                Parallel.ForEach(responses, (response) =>
                {
                    seriesDescriptionResponses.TryAdd(response.Key, response.Value);
                });
            }
        }

        private static void processSeriesDescriptionsResponses()
        {
            // process request response
            foreach (KeyValuePair<string, sdGenericDescriptions> response in seriesDescriptionResponses)
            {
                ++processedObjects; reportProgress();

                string series_id = response.Key.Replace("EP", "SH");
                sdGenericDescriptions description = response.Value;

                // determine which seriesInfo this belongs to
                MxfSeriesInfo mxfSeriesInfo = sdMxf.With[0].getSeriesInfo(series_id.Substring(2, 8));

                // populate descriptions
                mxfSeriesInfo.ShortDescription = description.Description100;
                mxfSeriesInfo.Description = description.Description1000;

                // serialize JSON directly to a file
                using (StringWriter writer = new StringWriter())
                {
                    try
                    {
                        JsonSerializer serializer = new JsonSerializer();
                        serializer.Serialize(writer, description);
                        epgCache.AddAsset(series_id, writer.ToString());
                    }
                    catch { }
                }
            }
        }

        private static bool buildAllExtendedSeriesDataForUiPlus()
        {
            // reset counters
            processedObjects = 0;
            ++processStage; reportProgress();
            seriesDescriptionQueue = new List<string>();

            if (!config.ModernMediaUiPlusSupport) return true;
            Logger.WriteMessage(string.Format("Entering buildAllExtendedSeriesDataForUiPlus() for {0} series.",
                                totalObjects = sdMxf.With[0].SeriesInfos.Count));

            // read in cached ui+ extended information
            Dictionary<string, ModernMediaUiPlusPrograms> oldPrograms = new Dictionary<string, ModernMediaUiPlusPrograms>();
            if (File.Exists(Helper.Epg123MmuiplusJsonPath))
            {
                using (StreamReader sr = new StreamReader(Helper.Epg123MmuiplusJsonPath))
                {
                    oldPrograms = JsonConvert.DeserializeObject<Dictionary<string, ModernMediaUiPlusPrograms>>(sr.ReadToEnd());
                }
            }

            // fill mxf programs with cached values and queue the rest
            foreach (MxfSeriesInfo series in sdMxf.With[0].SeriesInfos)
            {
                string seriesId = $"SH{series.tmsSeriesId}0000";

                // sports events will not have a generic description
                if (series.tmsSeriesId.StartsWith("SP"))
                {
                    ++processedObjects; reportProgress();
                    continue;
                }
                // generic series information already in support file array
                else if (ModernMediaUiPlus.Programs.ContainsKey(seriesId))
                {
                    ++processedObjects; reportProgress();
                    ModernMediaUiPlusPrograms program;
                    if (ModernMediaUiPlus.Programs.TryGetValue(seriesId, out program) && program.OriginalAirDate != null)
                    {
                        UpdateSeriesAirdate(seriesId, program.OriginalAirDate);
                    }
                    continue;
                }
                // extended information in current json file
                else if (oldPrograms.ContainsKey(seriesId))
                {
                    ++processedObjects; reportProgress();
                    ModernMediaUiPlusPrograms program;
                    if (oldPrograms.TryGetValue(seriesId, out program) && !string.IsNullOrEmpty(program.OriginalAirDate))
                    {
                        ModernMediaUiPlus.Programs.Add(seriesId, program);
                        UpdateSeriesAirdate(seriesId, program.OriginalAirDate);
                    }
                    continue;
                }

                // add to queue
                seriesDescriptionQueue.Add(seriesId);
            }
            Logger.WriteVerbose($"Found {processedObjects} cached extended series descriptions.");

            // maximum 5000 queries at a time
            if (seriesDescriptionQueue.Count > 0)
            {
                for (int i = 0; i < seriesDescriptionQueue.Count; i += MAXQUERIES)
                {
                    if (!GetExtendedSeriesDataForUiPlus(i))
                    {
                        Logger.WriteInformation("Problem occurred during buildAllExtendedSeriesDataForUiPlus(). Exiting.");
                        return true;
                    }
                }
            }
            Logger.WriteInformation($"Processed {processedObjects} extended series descriptions.");
            Logger.WriteMessage("Exiting buildAllExtendedSeriesDataForUiPlus(). SUCCESS.");
            return true;
        }
        private static bool GetExtendedSeriesDataForUiPlus(int start = 0)
        {
            // build the array of programs to request for
            string[] programs = new string[Math.Min(seriesDescriptionQueue.Count - start, MAXQUERIES)];
            for (int i = 0; i < programs.Length; ++i)
            {
                programs[i] = seriesDescriptionQueue[start + i];
            }

            // request programs from Schedules Direct
            IList<sdProgram> responses = sdAPI.sdGetPrograms(programs);
            if (responses == null) return false;

            // process request response
            int idx = 0;
            foreach (sdProgram response in responses)
            {
                if (response == null)
                {
                    Logger.WriteInformation($"Did not receive data for program {programs[idx++]}.");
                    continue;
                }
                ++idx; ++processedObjects; reportProgress();

                // add the series extended data to the file if not already present from program builds
                if (!ModernMediaUiPlus.Programs.ContainsKey(response.ProgramID))
                {
                    AddModernMediaUiPlusProgram(response);
                }

                // add the series start air date if available
                UpdateSeriesAirdate(response.ProgramID, response.OriginalAirDate);
            }
            return true;
        }
        private static void UpdateSeriesAirdate(string seriesId, string originalAirdate)
        {
            UpdateSeriesAirdate(seriesId, DateTime.Parse(originalAirdate));
        }
        private static void UpdateSeriesAirdate(string seriesId, DateTime airdate)
        {
            // write the mxf entry
            sdMxf.With[0].getSeriesInfo(seriesId.Substring(2, 8)).StartAirdate = airdate.ToString();

            // update cache if needed
            try
            {
                using (StringReader reader = new StringReader(epgCache.GetAsset(seriesId)))
                {
                    JsonSerializer serializer = new JsonSerializer();
                    sdGenericDescriptions cached = (sdGenericDescriptions)serializer.Deserialize(reader, typeof(sdGenericDescriptions));
                    if (string.IsNullOrEmpty(cached.StartAirdate))
                    {
                        cached.StartAirdate = airdate.Equals(DateTime.MinValue) ? "" : airdate.ToString("yyyy-MM-dd");
                        using (StringWriter writer = new StringWriter())
                        {
                            serializer.Serialize(writer, cached);
                            epgCache.UpdateAssetJsonEntry(seriesId, writer.ToString());
                        }
                    }
                }
            }
            catch { }
        }
    }
}