﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using vws.web.Domain._base;
using vws.web.Domain._team;

namespace vws.web.Domain._calendar
{
    [Table("Calendar_Event")]
    public class Event
    {
        public Event()
        {
            EventProjects = new HashSet<EventProject>();
            EventUsers = new HashSet<EventMember>();
        }

        public int Id { get; set; }

        public Guid Guid { get; set; }

        [MaxLength(500)]
        public string Title { get; set; }

        [MaxLength(2500)]
        public string Description { get; set; }

        public bool IsAllDay { get; set; }

        public int? TeamId { get; set; }

        public DateTime StartTime { get; set; }

        public DateTime EndTime { get; set; }

        public bool IsDeleted { get; set; }

        public DateTime CreatedOn { get; set; }

        public Guid CreatedBy { get; set; }

        public DateTime ModifiedOn { get; set; }

        public Guid ModifiedBy { get; set; }

        public virtual Team Team { get; set; }

        public virtual ICollection<EventProject> EventProjects { get; set; }

        public virtual ICollection<EventMember> EventUsers { get; set; }
    }

    class EventComparer : IEqualityComparer<Event>
    {
        public bool Equals(Event firstEvent, Event secondEvent)
        {

            if (Object.ReferenceEquals(firstEvent, secondEvent)) return true;

            if (Object.ReferenceEquals(firstEvent, null) || Object.ReferenceEquals(secondEvent, null))
                return false;

            return firstEvent.Id == secondEvent.Id;
        }

        public int GetHashCode(Event _event)
        {
            if (Object.ReferenceEquals(_event, null)) return 0;

            return _event.Id.GetHashCode();
        }
    }
}
