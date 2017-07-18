*********************
IN PROGRESS
Support a cache
	Make use of IsCache flag in backupstore
		use to indicate to user when they are browsing, backing up to the cache
			-shows when browsing (done)
			show other places on console as well
	Sync cache after all operations?
		Ability to explicitly sync cache (not just after another operation)
		Standardize API on auto or manual syncing
			In other words does calling a public method in Core cause a sync or must syncs be called manually once operations complete?
	Warn when deleting backups from destination when the cache is not present
		Cache depends on metadatatrees from previous backups for differential backups
			especially last backup (default)
	Eventually implement blob-level cache?
Code cleanup
	Rename all occurences of block to blob
	Roll MetadataNodeReferenceIterator into BlobReferenceIterator
	Just publicly use GetAllBlobReferences not GetBackupReferences
	Comments
	Remove MetaDataTree class
		just use root metadata node
		standardize on (no?) (a?) prefix for root
	Cleanup heirarchy of Core -> BackupStore, BlobStore
		Should the API and console app be seing BackupStore and BlobStore directly?
			If yes always access like Core.B___Store or pull out and use like own variable?
			If no make BackupStore and BlobStore protected and add needed public interfaces to Core **
Switch to existing argparser (ie from nuget)
Add more unit tests
	deteting backups and dereferencing
Support deleting entire backup stores (at dest)
Test support for large backup sets
	Optimize B+ tree BlobStore
		bulk loading of tree
		store some nodes out of memory?
		optimize node size
		Progress report/bar
	Reduce number of blob files per directory
		Currently one file per blob and all files in destination root
		(Some) operating systems have poor performance with many files in a single directory
Handle common things that could go wrong
	circular links when checking if ancestor is backup source
	safe writeout of indexes
		Write out old index without overwriting then rename
	when only one of backup or blob index is updated
		more generally any time they become out of sync
			"stamp" them with uuid so can detect out of sync?
	Save "progress" so can pick up after large backup halted
	more...
ArgParser
	"-l <>" => "-l <longname>"
		wont need Item3 of returned tuple
		create mapping from short to longnames
			only use longnames to access parsed data
	can require only one or at least one of a set of options
		'|' or '^' between options
		options inside [] ?
More flexible "list" command
	Ranges of n-m backups ago
		Also by date?
	Better handling of browsing list of many backups
.Net Core port
	Begin with .Net standard port of library code
		.Net standard library will work with Framework and Code versions of project
	Make primary instance of project?
	Test under linux
"Enhanced data"
	NTFS Permissions support
		Ability to escalate this application's own permissions
			Only when needed
	NTFS extended attributes
	Ability to save/restore enhanced or "dumb" data
		Save/restore w/ & w/o permissions
			Detect restore to machine without user/group corresponding to permissions being applied
		Save/restore w/ & w/o extended attributes
	Special link support
Generic permissions support ie. Linux (POSIX)
	Handle restoring to different OS/permissions scheme than saved to
Replicate source file tree (as last backed up) in destination
	BackupLocations point at these files
	Block in the backup store but not in the replicated tree stored in a seperate folder
	Easy step to a versioning system
		Data would have to be preduplicated to detect changes
			Unless used through a virtual filesystem
			Or could be used in tandem with a backup disk
				without backup disk would have access to all changes except the state of the latest backup
Reverse references in BlobStore?
	Every blob knows hash of every structure that points at it?
	Could list every backup containing file
	Use worth the complexity?

**********************
WHAT'S GOING ON
The goal of this program is to back files up quickly and efficiently (i.e. low disk usage)

The user runs the program from a base folder, and all files and child directories get backed up

When a file gets backed up, its metadata and data are seperated.

Data from a file is split into chunks (called blocks) that average ~4KB. For large files that change only a little, this allows only the modified parts to be saved again in the backup. The blocks are stored according to their SHA1 hashes. A hash index (<destination>/index/hashes) keeps track of which hashes (blocks) we have stored. In memory this is a very efficient B+ tree of hashes. Now we save each block as <destination>/<block hash>, but this may be done differently later. For that reason some extra data (relative path, byte offset, block length) is stored in the hash index.

Metadata from a file (right now we don't support all NTFS metadata) is stored, along with the metadata of all other files and directories being backed up, in a metadata index (<destination>/index/metadata). This index rembebers the original file tree of the backup source. The metadata index also contains, for each file, an ordered list of the hashes making up that file.

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