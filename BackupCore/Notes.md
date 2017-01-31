1. xdelta or equivalent on saved metadata/blocklist

2. *belay this* combine metadata/blocklist save as in original folder structure 

3. save metadata in tree structure (just like a filesystem)
	metadata = (filename,
				list of hashes describing content,
				size,
				revelent timestamps,
				[eventually full NTFS metadata including extended attributes])

*********************
IN PROGRESS
Browse past backups easily ("backup browse")
	Captive prompt?
		backup rel_path> ls (etc)
			simple to restore file located in this manner 
Reference count data blocks
	Delete backups
		Consider hashes as backup identifiers (like git)
		Backup id would then be "stable"
	Show total and "additional" space used by each backup
		additional=space regained by deleting backup and leaving all others
Progress report/bar
Ignore patterns
Handle common things that could go wrong
	warn when lack permission to backup file
	warn when overwriting existing file when restoring
	
	more...
Multiple base folders
Replicate source file tree (as last backed up) in destination
	BackupLocations point at these files
	Block in the backup store but not in the replicated tree stored in a seperate folder

Special handling for zip files (including Office .***x files)?
	Deduplication wont work for compressed formats (I think?)
	silently expand archive for saving
	Issues:
		compressed for a reason, deduplication may be far less efficient than compression for an edited paper, etc.

**********************
WHAT'S GOING ON
The goal of this program is to back files up quickly and efficiently (i.e. low disk usage)

The user runs the program from a base folder, and all files and child directories get backed up

When a file gets backed up, its metadata and data are seperated.

Data from a file is split into chunks (called blocks) that average ~4KB. For large files that change only a little, this allows only the modified parts to be saved again in the backup. The blocks are stored according to their SHA1 hashes. A hash index (<destination>/index/hashes) keeps track of which hashes (blocks) we have stored. In memory this is a very efficient B+ tree of hashes. Now we save each block as <destination>/<block hash>, but this may be done differently later. For that reason some extra data (relative path, byte offset, block length) is stored in the hash index.

Metadata from a file (right now we don't support all NTFS metadata) is stored, along with the metadata of all other files and directories being backed up, in a metadata index (<destination>/index/metadata). This index rembebers the original file tree of the backup source. The metadata index also contains, for each file, an ordered list of the hashes making up that file.