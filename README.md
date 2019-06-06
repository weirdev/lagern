# Lagern

Content addressable deduplicative backup system. Allows for fast and efficient storage of backups, including allowing deduplication when file based and block based backups of the same data are stored together. 

The goal of this program is to back files up quickly and efficiently (i.e. low disk usage)

The user runs the program from a base folder, and all files and child directories get backed up

When a file gets backed up, its metadata and data are seperated.

Data from a file is split into chunks (called blocks) that average ~4KB. For large files that change only a little, this allows only the modified parts to be saved again in the backup. The blocks are stored according to their SHA1 hashes. A hash index (<destination>/index/hashes) keeps track of which hashes (blocks) we have stored. In memory this is a very efficient B+ tree of hashes. Now we save each block as <destination>/<block hash>, but this may be done differently later. For that reason some extra data (relative path, byte offset, block length) is stored in the hash index.

Metadata from a file (right now we don't support all NTFS metadata) is stored, along with the metadata of all other files and directories being backed up, in a metadata index (<destination>/index/metadata). This index rembebers the original file tree of the backup source. The metadata index also contains, for each file, an ordered list of the hashes making up that file.

Backups can be performed "differentially". This is similar to, but different than, a "delta" backup in other backup utilities and version control systems. All lagern backups are "delta" backups in the sense that multiple backups may rely on the same blob of (deduplicated) data. However, unlike some "delta" implementations, deleting a lagern backup never deletes data relyed on by another backup.

A differential backup in lagern mostly refers to how files are chosen to be scanned for backing up. This allows us to sometimes avoid scanning a file's data. ie. "Only scan this file if it appears to have changed (date modified changed etc.)". This choice is made relative to a single existing backup. Files that are not scanned (but are not to be totally ignored from the backup) are still in the new backup. Their data is just assumed to be the same as in the selected existing backup. By default, lagern performs differential backups relative to the last backup in the selected destination.

Uses ranked list of add/ignore rules
		Foward slashes only for all systems
			Allow escaping spaces with \ ?
		By default, first rule is '2 *' = include everything in all subdirectories
		Rules later in the list get higher priority
		Trailing slash interpreted as /*
		<path>/* = all files and subdirectories
		Ex:
			2 *    #== /*
			0 /pictures
			3 /pictures/grandma/  #== /pictures/grandma/*
			Translates as -> include everything 2, except /pictures, but do include /picutes/grandma and its children 3
		Including a folder assumes including subfolders
		If files in subfolders not wanted
			2 dir/*
			0 dir/*/*
		Use patterns to classify files for checking for changes
		One list multiple classifications
			0 = Dont add at all
			1 = File data only scanned on first backup
				Changes ignored
			2 = Scanned only based on metadata heuristics (default behavior when no lists present)
				Date modified or length changed so scan
			3 = Scanned every time regardless of metadata
				Behavior of 1,2, and 3 when force scan switch is used

Current performance results
Time simple copy vs backup run
		1000 files
		1,000,000 bytes each
		~0.5 MB index overhead
		Simple copy
			38.625 seconds
		Synchronous backup run (Release)
			memory usage always <24MB
			148.547 seconds
			low cpu usage
				majority of cpu time spent splitting files
			Update (metadata should yeild no scanning needed)
				Near instantaneous
		Asynchronous backup run
			Memory usage ~50 MB
			83.828 seconds
			cpu usage choppy, averages about 3/4 utilization