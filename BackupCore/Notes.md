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
Remember past filesystem states
Add FileAttributes to FileMetadata
Browse past backups easily ("backup ls")