﻿// Copyright (c) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Practices.EnterpriseLibrary.SemanticLogging.Utility;

namespace Microsoft.Practices.EnterpriseLibrary.SemanticLogging.Schema
{
    /// <summary>
    /// Parses the ETW manifest generated by the <see cref="EventSource"/> class.
    /// </summary>
    public class EventSourceSchemaReader
    {
        private static readonly XNamespace Ns = "http://schemas.microsoft.com/win/2004/08/events";
        private static readonly XName Root = Ns + "instrumentationManifest";
        private static readonly XName Instrumentation = Ns + "instrumentation";
        private static readonly XName Events = Ns + "events";
        private static readonly XName Provider = Ns + "provider";
        private static readonly XName Tasks = Ns + "tasks";
        private static readonly XName Task = Ns + "task";
        private static readonly XName Keywords = Ns + "keywords";
        private static readonly XName Keyword = Ns + "keyword";
        private static readonly XName Opcodes = Ns + "opcodes";
        private static readonly XName Opcode = Ns + "opcode";
        private static readonly XName Event = Ns + "event";
        private static readonly XName Templates = Ns + "templates";
        private static readonly XName Template = Ns + "template";

        private static readonly Regex DotNetTokensRegex = new Regex(@"%(\d+)", RegexOptions.Compiled);
        private static readonly Regex StringIdRegex = new Regex(@"\$\(string\.(.+?)\)", RegexOptions.Compiled);

        /// <summary>
        /// Gets the schema for the specified event source.
        /// </summary>
        /// <param name="eventSource">The event source.</param>
        /// <returns>The event schema.</returns>
        public IDictionary<int, EventSchema> GetSchema(EventSource eventSource)
        {
            Guard.ArgumentNotNull(eventSource, "eventSource");

            return this.GetSchema(EventSource.GenerateManifest(eventSource.GetType(), null));
        }

        internal IDictionary<int, EventSchema> GetSchema(string manifest)
        {
            var doc = XDocument.Parse(manifest);

            var provider = doc.Root.Element(Instrumentation).Element(Events).Element(Provider);
            var templates = provider.Element(Templates);
            var tasks = provider.Element(Tasks);
            var opcodes = provider.Element(Opcodes);
            var keywords = provider.Element(Keywords);
            ////var stringTable = GetStringTable(doc.Root);

            var providerGuid = (Guid)provider.Attribute("guid");
            var providerName = (string)provider.Attribute("name");
            var events = new Dictionary<int, EventSchema>();

            foreach (var @event in provider.Element(Events).Elements(Event))
            {
                var eventId = (int)@event.Attribute("value");
                var templateRef = @event.Attribute("template");
                var taskName = (string)@event.Attribute("task");

                int taskId = 0;
                if (!string.IsNullOrWhiteSpace(taskName))
                {
                    taskId = (int)tasks
                        .Elements(Task)
                        .First(t => (string)t.Attribute("name") == taskName)
                        .Attribute("value");
                }

                var level = this.ParseLevel((string)@event.Attribute("level"));
                var opcode = this.ParseOpcode((string)@event.Attribute("opcode"), opcodes);
                ////var message = GetLocalizedString((string)@event.Attribute("message"), stringTable);

                var keywordNames = (string)@event.Attribute("keywords");

                long keywordsMask = 0;
                if (!string.IsNullOrWhiteSpace(keywordNames))
                {
                    foreach (var keywordName in keywordNames.Split())
                    {
                        var keywordsMaskAtt = keywords
                            .Elements(Keyword)
                            .Where(k => (string)k.Attribute("name") == keywordName)
                            .Select(k => k.Attribute("mask"))
                            .FirstOrDefault();

                        if (keywordsMaskAtt != null)
                        {
                            keywordsMask |= Convert.ToInt64(keywordsMaskAtt.Value, 16);
                        }
                    }
                }

                int version = @event.Attribute("version") != null ? (int)@event.Attribute("version") : 0;

                IEnumerable<string> paramList;
                if (templateRef == null)
                {
                    // Event has no parameters/payload
                    paramList = Enumerable.Empty<string>();
                }
                else
                {
                    paramList = templates
                                        .Elements(Template)
                                        .First(t => (string)t.Attribute("tid") == templateRef.Value)
                                            .Elements(Ns + "data")
                                            .Select(d => (string)d.Attribute("name")).ToList();
                }

                events.Add(eventId,
                        new EventSchema(
                            eventId,
                            providerGuid,
                            providerName,
                            level,
                            (EventTask)taskId,
                            taskName,
                            opcode.Item2,
                            opcode.Item1,
                            ////message,
                            (EventKeywords)keywordsMask,
                            keywordNames,
                            version,
                            paramList));
            }

            return events;
        }

        public static EventSchema GetDynamicSchema(EventWrittenEventArgs eventData)
        {
            // TODO: Validate that the only event id this method
            // is used for is -1 (for dynamic events)?
            return new EventSchema(
                            eventData.EventId,
                            eventData.EventSource.Guid,
                            eventData.EventSource.Name,
                            eventData.Level,
                            eventData.Task,
                            eventData.Task.ToString(),
                            eventData.Opcode,
                            eventData.Opcode.ToString(),
                            ////message,
                            eventData.Keywords,
                            eventData.Keywords.ToString(),
                            0, // Dynamic events don't have a version.
                            eventData.PayloadNames);
        }
        
        private EventLevel ParseLevel(string level)
        {
            switch (level)
            {
                case "win:Critical":
                    return EventLevel.Critical;
                case "win:Error":
                    return EventLevel.Error;
                case "win:Warning":
                    return EventLevel.Warning;
                case "win:Informational":
                    return EventLevel.Informational;
                case "win:Verbose":
                    return EventLevel.Verbose;
                case "win:LogAlways":
                default:
                    return EventLevel.LogAlways;
            }
        }

        ////private static string GetLocalizedString(string format, XElement stringTable)
        ////{
        ////    if (string.IsNullOrWhiteSpace(format) || stringTable == null)
        ////    {
        ////        return format;
        ////    }

        ////    return stringIdRegex.Replace(format, match =>
        ////    {
        ////        if (match.Groups.Count == 2)
        ////        {
        ////            var stringValue = FindString(stringTable, match.Groups[1].Value);
        ////            if (stringValue != null)
        ////            {
        ////                stringValue = ReplaceTokens(stringValue);
        ////                return stringValue;
        ////            }
        ////        }

        ////        return match.Value;
        ////    });
        ////}

        ////private static string ReplaceTokens(string format)
        ////{
        ////    return dotNetTokensRegex.Replace(format, ReplaceDotNetToken);
        ////}

        ////private static string ReplaceDotNetToken(Match match)
        ////{
        ////    if (match.Groups.Count == 2)
        ////    {
        ////        var position = int.Parse(match.Groups[1].Value);
        ////        return "{" + (position - 1) + "}";
        ////    }

        ////    return match.Value;
        ////}

        ////private static string FindString(XElement stringTable, string id)
        ////{
        ////    var stringElement = stringTable.Elements(ns + "string")
        ////        .FirstOrDefault(s => (string)s.Attribute("id") == id);
        ////    if (stringElement != null)
        ////    {
        ////        return (string)stringElement.Attribute("value");
        ////    }

        ////    return null;
        ////}

        ////private static XElement GetStringTable(XElement instrumentation)
        ////{
        ////    var element = instrumentation.Element(ns + "localization");
        ////    if (element != null)
        ////    {
        ////        element = element.Element(ns + "resources"); // TODO: filter by language?
        ////        if (element != null)
        ////        {
        ////            return element.Element(ns + "stringTable");
        ////        }
        ////    }

        ////    return null;
        ////}

        private Tuple<string, EventOpcode> ParseOpcode(string opcode, XElement opcodes)
        {
            switch (opcode)
            {
                case null:
                case "win:Info":
                    return Tuple.Create("Info", EventOpcode.Info);
                case "win:Start":
                    return Tuple.Create("Start", EventOpcode.Start);
                case "win:Stop":
                    return Tuple.Create("Stop", EventOpcode.Stop);
                case "win:DC_Start":
                    return Tuple.Create("DC_Start", EventOpcode.DataCollectionStart);
                case "win:DC_Stop":
                    return Tuple.Create("DC_Stop", EventOpcode.DataCollectionStop);
                case "win:Extension":
                    return Tuple.Create("Extension", EventOpcode.Extension);
                case "win:Reply":
                    return Tuple.Create("Reply", EventOpcode.Reply);
                case "win:Resume":
                    return Tuple.Create("Resume", EventOpcode.Resume);
                case "win:Suspend":
                    return Tuple.Create("Suspend", EventOpcode.Suspend);
                case "win:Send":
                    return Tuple.Create("Send", EventOpcode.Send);
                case "win:Receive":
                    return Tuple.Create("Receive", EventOpcode.Receive);
            }

            if (!string.IsNullOrWhiteSpace(opcode))
            {
                var opcodeElement = opcodes.Elements(Opcode).FirstOrDefault(o => (string)o.Attribute("name") == opcode);
                if (opcodeElement != null)
                {
                    int opcodeId = (int)opcodeElement.Attribute("value");
                    return Tuple.Create(opcode, (EventOpcode)opcodeId);
                }
            }

            return Tuple.Create("Info", EventOpcode.Info);
        }
    }
}
