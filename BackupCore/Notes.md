*********************
IN PROGRESS
Ignore/Save patterns
	Git like system for tracking?
	Use patterns to classify files for checking for changes
		Some files data only scanned if added manually
		Some scanned only based on metadata heuristics (default)
			Date modified changed so scan
		Some scanned every time regardless of metadata
			Always do this when force scan switch is used
Specify a previous backup to use as previous backup when performing a differential backup
	Currently we just use the last backup made
Handle common things that could go wrong
	warn when lack permission to backup file
	warn when overwriting existing file when restoring
	crash mid operations prints error
	more...
Test support for large backup sets
	Optimize B+ tree BlobStore
		bulk loading of tree
		store some nodes out of memory?
		optimize node size
		Progress report/bar
ArgParser
	"-l <>" => "-l <longname>"
		wont need Item3 of returned tuple
		create mapping from short to longnames
			only use longnames to access parsed data
	can require only one or at least one of a set of options
		'|' or '^' between options
		options inside [] ?
.Net Core port
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
Multiple base folders
	Multiple bacups with different settings to same destination
		"dumb" binary backup of entire partition
		"smart" backup of files folders
		Data deduplicated itra- and inter- backup source
			Restore/browse functionality handles multiple backups folders somewhat like seperate drives in NTFS
Transfer a single backup to existing/new different backup destination
	Would show up as new base folder
Generic permissions support ie. Linux (POSIX)
	Handle restoring to different OS/permissions scheme than saved to
Replicate source file tree (as last backed up) in destination
	BackupLocations point at these files
	Block in the backup store but not in the replicated tree stored in a seperate folder
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
