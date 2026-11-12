CREATE TABLE IF NOT EXISTS `limits` (
	`key` text PRIMARY KEY NOT NULL,
	`count` integer NOT NULL
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS `members` (
	`room` text NOT NULL,
	`id` text NOT NULL,
	`proof` text NOT NULL,
	`name` text NOT NULL,
	`chests` integer DEFAULT 0 NOT NULL,
	`rewards` text DEFAULT '{}' NOT NULL,
	`revision` integer DEFAULT 0 NOT NULL,
	`updated` integer NOT NULL,
	`seen` integer NOT NULL,
	`active` integer DEFAULT 1 NOT NULL,
	PRIMARY KEY(`room`, `id`)
);
--> statement-breakpoint
CREATE TABLE IF NOT EXISTS `rooms` (
	`id` text PRIMARY KEY NOT NULL,
	`name` text NOT NULL,
	`join_hash` text NOT NULL,
	`admin_hash` text NOT NULL,
	`created` integer NOT NULL
);
