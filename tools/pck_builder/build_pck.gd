extends SceneTree

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 2:
		push_error("Usage: -- <source_manifest_path> <output_pck_path>")
		quit(1)
		return

	var source_manifest := args[0]
	var output_pck := args[1]
	var packer := PCKPacker.new()

	var start_error := packer.pck_start(output_pck)
	if start_error != OK:
		push_error("Failed to start PCK packer: %s" % start_error)
		quit(1)
		return

	var add_error := packer.add_file("res://mod_manifest.json", source_manifest)
	if add_error != OK:
		push_error("Failed to add mod_manifest.json: %s" % add_error)
		quit(1)
		return

	var mod_root_name := source_manifest.get_base_dir().get_file()
	var manifest_file := FileAccess.open(source_manifest, FileAccess.READ)
	if manifest_file != null:
		var manifest_payload = JSON.parse_string(manifest_file.get_as_text())
		if manifest_payload is Dictionary:
			var manifest_pck_name = str(manifest_payload.get("pck_name", "")).strip_edges()
			if not manifest_pck_name.is_empty():
				mod_root_name = manifest_pck_name
	var mod_image_path := source_manifest.get_base_dir().path_join("mod_image.png")
	if FileAccess.file_exists(mod_image_path):
		add_error = packer.add_file("res://%s/mod_image.png" % mod_root_name, mod_image_path)
		if add_error != OK:
			push_error("Failed to add mod_image.png: %s" % add_error)
			quit(1)
			return

	var flush_error := packer.flush()
	if flush_error != OK:
		push_error("Failed to finalize PCK: %s" % flush_error)
		quit(1)
		return

	print("Packed %s" % output_pck)
	quit(0)
