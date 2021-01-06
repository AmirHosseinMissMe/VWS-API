﻿// <auto-generated />
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using vws.web.Models.Context.chat;

namespace vws.web.Migrations.MessageDb
{
    [DbContext(typeof(MessageDbContext))]
    [Migration("20210106133723_addChatMessageDbContext")]
    partial class addChatMessageDbContext
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .UseIdentityColumns()
                .HasAnnotation("Relational:MaxIdentifierLength", 128)
                .HasAnnotation("ProductVersion", "5.0.1");

            modelBuilder.Entity("vws.web.Models.Context.chat.Message", b =>
                {
                    b.Property<long>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .UseIdentityColumn();

                    b.Property<string>("Body")
                        .HasColumnType("nvarchar(max)");

                    b.Property<byte>("MessageTypeId")
                        .HasColumnType("tinyint");

                    b.HasKey("Id");

                    b.HasIndex("MessageTypeId");

                    b.ToTable("Messages");
                });

            modelBuilder.Entity("vws.web.Models.Context.chat.MessageType", b =>
                {
                    b.Property<byte>("Id")
                        .HasColumnType("tinyint");

                    b.HasKey("Id");

                    b.ToTable("MessageTypes");
                });

            modelBuilder.Entity("vws.web.Models.Context.chat.Message", b =>
                {
                    b.HasOne("vws.web.Models.Context.chat.MessageType", "MessageType")
                        .WithMany()
                        .HasForeignKey("MessageTypeId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("MessageType");
                });
#pragma warning restore 612, 618
        }
    }
}
