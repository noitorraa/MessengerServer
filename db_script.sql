ALTER DATABASE CHARACTER SET utf8mb4;


CREATE TABLE `chats` (
    `chat_id` int NOT NULL AUTO_INCREMENT,
    `chat_name` varchar(100) CHARACTER SET utf8 COLLATE utf8_general_ci NULL,
    `created_at` datetime NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`chat_id`)
) CHARACTER SET=utf8 COLLATE=utf8_general_ci;


CREATE TABLE `Files` (
    `FileId` int NOT NULL AUTO_INCREMENT,
    `FileName` text CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
    `FileType` text CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
    `FileData` MEDIUMBLOB NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`FileId`)
) CHARACTER SET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;


CREATE TABLE `users` (
    `user_id` int NOT NULL AUTO_INCREMENT,
    `username` varchar(50) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
    `password_hash` varchar(255) CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
    `created_at` datetime NULL DEFAULT CURRENT_TIMESTAMP,
    `phoneNumber` text CHARACTER SET utf8 COLLATE utf8_general_ci NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`user_id`)
) CHARACTER SET=utf8 COLLATE=utf8_general_ci;


CREATE TABLE `chat_members` (
    `id` int NOT NULL AUTO_INCREMENT,
    `chat_id` int NOT NULL,
    `user_id` int NOT NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`id`),
    CONSTRAINT `FK_chat_members_chats` FOREIGN KEY (`chat_id`) REFERENCES `chats` (`chat_id`),
    CONSTRAINT `FK_chat_members_users` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`)
) CHARACTER SET=utf8 COLLATE=utf8_general_ci;


CREATE TABLE `messages` (
    `message_id` int NOT NULL AUTO_INCREMENT,
    `chat_id` int NULL,
    `sender_id` int NULL,
    `content` text CHARACTER SET utf8 COLLATE utf8_general_ci NOT NULL,
    `created_at` datetime NULL DEFAULT CURRENT_TIMESTAMP,
    `FileId` int NULL,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`message_id`),
    CONSTRAINT `FK_messages_chats` FOREIGN KEY (`chat_id`) REFERENCES `chats` (`chat_id`),
    CONSTRAINT `FK_messages_users` FOREIGN KEY (`sender_id`) REFERENCES `users` (`user_id`),
    CONSTRAINT `messages_ibfk_1` FOREIGN KEY (`FileId`) REFERENCES `Files` (`FileId`)
) CHARACTER SET=utf8 COLLATE=utf8_general_ci;


CREATE TABLE `message_statuses` (
    `status_id` int NOT NULL AUTO_INCREMENT,
    `message_id` int NOT NULL,
    `user_id` int NULL,
    `status` int NOT NULL,
    `updated_at` datetime NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT `PRIMARY` PRIMARY KEY (`status_id`),
    CONSTRAINT `FK_message_statuses_users` FOREIGN KEY (`user_id`) REFERENCES `users` (`user_id`),
    CONSTRAINT `message_statuses_ibfk_1` FOREIGN KEY (`message_id`) REFERENCES `messages` (`message_id`)
) CHARACTER SET=utf8 COLLATE=utf8_general_ci;


CREATE INDEX `FK_chat_members_chats` ON `chat_members` (`chat_id`);


CREATE INDEX `FK_chat_members_users` ON `chat_members` (`user_id`);


CREATE INDEX `FK_message_statuses_users` ON `message_statuses` (`user_id`);


CREATE INDEX `message_id` ON `message_statuses` (`message_id`);


CREATE INDEX `FileId` ON `messages` (`FileId`);


CREATE INDEX `FK_messages_chats` ON `messages` (`chat_id`);


CREATE INDEX `FK_messages_users` ON `messages` (`sender_id`);


CREATE UNIQUE INDEX `username` ON `users` (`username`);


